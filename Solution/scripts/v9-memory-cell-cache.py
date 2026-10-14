#!/usr/bin/env python3
"""Reuse projected V9 blocking cells across many configurations in one process.

Diagnostic only: no SQL writes, no FS/TF/LLR changes. Memory budget is explicit.
"""
from collections import defaultdict
import itertools

class CellCache:
    def __init__(self, rows, max_pairs=2_000_000):
        if max_pairs < 1:
            raise ValueError("max_pairs must be positive")
        self.rows = list(rows)
        self.max_pairs = max_pairs
        self.cache = {}
        self.ids = [str(row["id"]) for row in self.rows]
        if len(self.ids) != len(set(self.ids)):
            raise ValueError("duplicate observation IDs")
        self.truth = defaultdict(list)
        for row in self.rows:
            if row.get("truth_id") is not None:
                self.truth[(row.get("split"), str(row["truth_id"]))].append(str(row["id"]))
        self.eligible_m = sum(len(group) * (len(group)-1)//2 for group in self.truth.values())

    def cells(self, fields):
        key = tuple(fields)
        if not key:
            raise ValueError("empty fields")
        if key not in self.cache:
            groups = defaultdict(list)
            for row in self.rows:
                values = tuple(str(row.get(field) or "").strip().upper() for field in key)
                if all(values):
                    groups[(row.get("split"), values)].append(str(row["id"]))
            self.cache[key] = tuple(tuple(group) for group in groups.values())
        return self.cache[key]

    def evaluate(self, passes, cap):
        if cap < 2:
            raise ValueError("cap must be >= 2")
        seen = set()
        increments = []
        for fields in passes:
            new_m = new_u = suppressed = 0
            for group in self.cells(fields):
                if len(group) > cap:
                    suppressed += 1
                    continue
                for left, right in itertools.combinations(group, 2):
                    pair = tuple(sorted((left, right)))
                    if pair in seen:
                        continue
                    if len(seen) >= self.max_pairs:
                        raise MemoryError("candidate-pair budget exceeded")
                    seen.add(pair)
                    new_m += self._same_truth(left, right)
                    new_u += not self._same_truth(left, right)
            increments.append({"fields": list(fields), "new_m": new_m,
                               "new_u": new_u, "suppressed_cells": suppressed})
        captured = sum(x["new_m"] for x in increments)
        return {"eligible_m": self.eligible_m, "captured_m": captured,
                "unique_candidates": len(seen),
                "recall": captured / self.eligible_m if self.eligible_m else None,
                "passes": increments, "cached_field_sets": len(self.cache)}

    def _same_truth(self, a, b):
        if not hasattr(self, "_by_id"):
            self._by_id = {str(row["id"]): (row.get("split"), row.get("truth_id"))
                           for row in self.rows}
        left, right = self._by_id[a], self._by_id[b]
        return left[1] is not None and left == right
