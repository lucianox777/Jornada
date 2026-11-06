"""Reconstruction invariants for the 81-state V2/V3 TF0 diagnostic."""
import importlib.util
from pathlib import Path
import unittest

SCRIPT=Path(__file__).resolve().parents[2]/"scripts"/"v9-mu-tf0-cell-ablation.py"
spec=importlib.util.spec_from_file_location("tf0cell",SCRIPT)
subject=importlib.util.module_from_spec(spec)
spec.loader.exec_module(subject)


def fixture():
    cells=[]
    for i in range(9):
        for j in range(9):
            cells.append({"Person":i,"Mother":j,
                "ObservedM":100 if (i,j)==(1,1) else 0,
                "ObservedU":100 if (i,j)==(7,7) else 0,
                "MethodM":"JOINT_DIRICHLET" if (i,j)==(1,1) else
                    "CONDITIONAL_PRODUCT_MARGINALS",
                "MethodU":"JOINT_DIRICHLET" if (i,j)==(7,7) else
                    "CONDITIONAL_PRODUCT_MARGINALS"})
    alpha=.5
    den=100+81*alpha
    def scale(class_key,point):
        marg_i=[0]*9;marg_j=[0]*9
        for c in cells:
            marg_i[c["Person"]]+=c[class_key]
            marg_j[c["Mother"]]+=c[class_key]
        residual=sum((c[class_key]+alpha)/den for c in cells
                     if (c["Person"],c["Mother"])!=point)
        product=sum((marg_i[c["Person"]]+9*alpha)/den*
                    (marg_j[c["Mother"]]+9*alpha)/den
                    for c in cells if (c["Person"],c["Mother"])!=point)
        return residual/product
    support={
        "Cells":cells,"TotalM":100,"TotalU":100,
        "SmoothingAlpha":alpha,
        "FallbackScaleM":scale("ObservedM",(1,1)),
        "FallbackScaleU":scale("ObservedU",(7,7)),
        "Policy":{"ClassMassCoverage":.99,"MinimumM":5,"MinimumU":5,
                  "SupportedTailRule":"RETAIN_CLASS_JOINT_WHEN_COUNT_AT_LEAST_K_V2"},
    }
    default={"smoothingAlpha":alpha,
             "observedSupportAudit":{"Cells":[{
                 "Person":c["Person"],"Mother":c["Mother"],
                 "ObservedMPairs":c["ObservedM"],
                 "ObservedUPairs":c["ObservedU"]} for c in cells]}}
    v3={"smoothingAlpha":alpha,"estimatorJointSupport":support,
        "MuPolicy":support["Policy"]}
    return default,v3


class TestCellAblation(unittest.TestCase):
    def test_81_cells_are_reconstructed_and_normalized(self):
        d,v=fixture()
        o=subject.compare_rows(d,v)
        self.assertEqual(len(o["cells"]),81)
        self.assertEqual(o["totalM"],100)
        self.assertEqual(o["totalU"],100)
        self.assertGreater(o["changedLikelihoodCells"],0)
        self.assertEqual(sum(o["mMethodCounts"].values()),81)
        self.assertEqual(sum(o["uMethodCounts"].values()),81)

    def test_missing_support_or_altered_counts_fail_closed(self):
        for mutate in (
            lambda d,v:v["estimatorJointSupport"]["Cells"].pop(),
            lambda d,v:v["estimatorJointSupport"].update(TotalU=99),
            lambda d,v:v["estimatorJointSupport"]["Cells"][0].update(ObservedU=1),
            lambda d,v:v["estimatorJointSupport"]["Cells"][0].update(MethodU="UNKNOWN"),
            lambda d,v:v["estimatorJointSupport"].update(FallbackScaleU=0),
            lambda d,v:v.update(MuPolicy={"ClassMassCoverage":.95}),
        ):
            with self.subTest(mutate=mutate):
                d,v=fixture()
                mutate(d,v)
                with self.assertRaises(ValueError):
                    subject.compare_rows(d,v)

    def test_truth_label_and_identity_data_are_not_inputs(self):
        d,v=fixture()
        self.assertEqual(set(d),{"smoothingAlpha","observedSupportAudit"})
        self.assertEqual(set(v),{"smoothingAlpha","estimatorJointSupport","MuPolicy"})
        o=subject.compare_rows(d,v)
        self.assertTrue(all("BaseIdentity" not in x and "scoreSource" not in x
                            for x in o["cells"]))


if __name__=="__main__":
    unittest.main()
