using System.Globalization;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicationClinicalContextProjectionService
{
    private readonly IEvidenceRepository _evidence;

    public VeteransReviewerMedicationClinicalContextProjectionService(
        IEvidenceRepository evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        _evidence = evidence;
    }

    public async Task<IReadOnlyList<VeteransReviewerMedicationClinicalContext>>
        GetAsync(
            IReadOnlyCollection<MedicationClinicalContextLink> links,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(links);

        var result =
            new List<VeteransReviewerMedicationClinicalContext>(links.Count);

        foreach (var link in links)
        {
            ArgumentNullException.ThrowIfNull(link);

            var context = link.Context;

            if (string.IsNullOrWhiteSpace(context.RecordTitle))
            {
                throw new InvalidDataException(
                    "Medication clinical context requires a human-readable record title before reviewer projection.");
            }

            var artifact =
                await _evidence.GetArtifactAsync(
                    context.SourceArtifactId,
                    cancellationToken)
                ?? throw new InvalidDataException(
                    "Medication clinical context source artifact was not found.");

            result.Add(
                new VeteransReviewerMedicationClinicalContext
                {
                    MedicationName = link.Medication.MedicationName,
                    ContextType = context.ContextType,
                    PrescriptionNumber = link.Medication.PrescriptionNumber!,
                    SourceLocator =
                        $"{GetHumanSourceName(artifact)} — " +
                        $"{context.RecordTitle.Trim()} — " +
                        $"{context.EventDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}",
                    Summary = context.Summary.Trim()
                });
        }

        return result;
    }

    private static string GetHumanSourceName(Artifact artifact)
    {
        if (artifact.Name.Contains(
                "Blue-Button",
                StringComparison.OrdinalIgnoreCase) ||
            artifact.Name.Contains(
                "Blue Button",
                StringComparison.OrdinalIgnoreCase))
        {
            return "VA Blue Button Report";
        }

        return VeteransReviewerDisplayNameResolver.Resolve(
            "Medical Record",
            artifact.Name);
    }
}
