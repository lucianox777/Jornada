// Shared immutable V9 cohort row, used by Worker TRAIN/VALIDATION and by Runner TEST.
// The BasePersonId is a synthetic truth label, never used to form candidate groups.
namespace Jornada.Linkage.Parameters.Worker;

public sealed record V9PhysicalCalibrationObservation(
    int ObservationId,
    string BasePersonId,
    string Partition,
    string Gestor,
    string? PersonName,
    string? MotherName,
    DateOnly? BirthDate);

