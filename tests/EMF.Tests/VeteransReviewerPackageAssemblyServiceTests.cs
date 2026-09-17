using System.Reflection;
using EMF.Core.Contracts;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Persistence.Repositories;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageAssemblyServiceTests
{
    [Fact]
    public async Task AssembleAsync_ComposesExistingReviewerServices()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var veteran = new Veteran { Id = new VeteranId("veteran-1") };
            await new SqliteVeteranRepository(path).AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-1"),
                VeteranId = veteran.Id
            };
            await new SqliteClaimRepository(path).AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };
            await new SqliteClaimIssueRepository(path).AddClaimIssueAsync(issue);

            var packageId = new EvidencePackageId("package-1");

            var packageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage
                {
                    Id = packageId,
                    ClaimIssueId = issue.Id,
                    Purpose = "Medical review",
                    ReviewerRole = "MedicalProfessional"
                },
                Artifacts = []
            };

            var packageService =
                DispatchProxy.Create<
                    IEvidencePackageService,
                    EvidencePackageServiceProxy>();

            ((EvidencePackageServiceProxy)(object)packageService).Details =
                packageDetails;

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var medicationRepository =
                new SqliteMedicationRepository(path);

            var ledger = new MedicationLedger
            {
                Id = new MedicationLedgerId("ledger-1"),
                VeteranId = veteran.Id,
                SourceArtifactId = new ArtifactId("blue-button"),
                ReportDate = new DateOnly(2026, 9, 15),
                SourceStartPage = 1,
                SourceEndPage = 1,
                ReportedEntryCount = 1,
                ParsedEntryCount = 1,
                IsComplete = true
            };

            var medication = new MedicationLedgerEntry
            {
                Id = new MedicationLedgerEntryId("medication-entry-1"),
                MedicationLedgerId = ledger.Id,
                EntryOrdinal = 1,
                SourceStartPage = 1,
                SourceEndPage = 1,
                MedicationName = "Trazodone",
                Strength = "100 mg",
                Status = "active",
                Directions = "TAKE THREE TABLETS ORALLY AT BEDTIME"
            };

            await medicationRepository.AddMedicationLedgerAsync(
                ledger,
                [medication]);

            await medicationRepository
                .AddMedicationCurrentUseReconciliationAsync(
                    new MedicationCurrentUseReconciliation
                    {
                        Id =
                            new MedicationCurrentUseReconciliationId(
                                "reconciliation-1"),
                        VeteranId = veteran.Id,
                        MedicationLedgerEntryId = medication.Id,
                        ReconciliationDate = new DateOnly(2026, 9, 15),
                        CurrentUseStatus =
                            MedicationCurrentUseStatuses.CurrentlyUsed,
                        Source = "VeteranReported"
                    });

            var clarificationRepository =
                new SqliteSourceClarificationRepository(path);
            await clarificationRepository.InitializeAsync();

            var progressionRepository =
                new SqliteClinicalProgressionRepository(path);
            await progressionRepository.InitializeAsync();

            var service = CreateService(
                path,
                packageService,
                evidence,
                medicationRepository,
                clarificationRepository,
                progressionRepository);

            var result =
                await service.AssembleAsync(
                    packageId,
                    "Michael Gould");

            Assert.NotNull(result);
            Assert.Equal("Michael Gould", result.PackagePreparedBy);
            Assert.Same(packageDetails, result.PackageDetails);
            Assert.Empty(result.Artifacts);
            Assert.Empty(result.ArtifactContents);

            var currentMedication =
                Assert.Single(result.CurrentMedications);

            Assert.Equal(medication.Id, currentMedication.Id);
            Assert.Equal("Trazodone", currentMedication.MedicationName);
            Assert.Empty(result.MedicationProgressions);
            Assert.Empty(result.MedicationClinicalContexts);
            Assert.Empty(result.SourceClarifications);
            Assert.Empty(result.ClinicalProgressionEvents);
            Assert.Null(result.MedicalOpinionRequested);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AssembleAsync_MissingPackageReturnsNull()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var packageService =
                DispatchProxy.Create<
                    IEvidencePackageService,
                    EvidencePackageServiceProxy>();

            var evidence = new SqliteEvidenceRepository(path);
            await evidence.InitializeAsync();

            var medicationRepository =
                new SqliteMedicationRepository(path);

            var clarificationRepository =
                new SqliteSourceClarificationRepository(path);
            await clarificationRepository.InitializeAsync();

            var progressionRepository =
                new SqliteClinicalProgressionRepository(path);
            await progressionRepository.InitializeAsync();

            var service = CreateService(
                path,
                packageService,
                evidence,
                medicationRepository,
                clarificationRepository,
                progressionRepository);

            var result =
                await service.AssembleAsync(
                    new EvidencePackageId("missing-package"));

            Assert.Null(result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static VeteransReviewerPackageAssemblyService CreateService(
        string path,
        IEvidencePackageService packageService,
        IEvidenceRepository evidence,
        SqliteMedicationRepository medicationRepository,
        SqliteSourceClarificationRepository clarificationRepository,
        SqliteClinicalProgressionRepository progressionRepository) =>
        new(
            new VeteransReviewerPackageDetailsService(
                packageService,
                evidence),
            new VeteransReviewerPackageCurrentMedicationService(
                new SqliteClaimIssueRepository(path),
                new SqliteClaimRepository(path),
                new ReconciledCurrentMedicationLedgerService(
                    new CurrentMedicationLedgerService(
                        medicationRepository),
                    medicationRepository)),
            new VeteransReviewerPackageMedicationProgressionService(
                new SqliteClaimIssueRepository(path),
                new SqliteClaimRepository(path),
                new SqliteServiceConnectionRepository(path),
                medicationRepository),
            new VeteransReviewerPackageMedicationClinicalContextService(
                new SqliteClaimIssueRepository(path),
                new SqliteClaimRepository(path),
                medicationRepository,
                evidence),
            new VeteransReviewerPackageSourceClarificationService(
                clarificationRepository),
            new VeteransReviewerPackageClinicalProgressionService(
                progressionRepository),
            new VeteransReviewerMedicalOpinionRequestService(
                new SqliteServiceConnectionRepository(path),
                new SqliteConditionRepository(path),
                new SqliteRegulatoryRepository(path)));

    public class EvidencePackageServiceProxy : DispatchProxy
    {
        public EvidencePackageDetails? Details { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name == "GetAsync" &&
                targetMethod.ReturnType ==
                    typeof(Task<EvidencePackageDetails>))
            {
                return Task.FromResult(Details);
            }

            throw new NotSupportedException(
                $"Unexpected evidence package service call: " +
                $"{targetMethod.Name}.");
        }
    }
}
