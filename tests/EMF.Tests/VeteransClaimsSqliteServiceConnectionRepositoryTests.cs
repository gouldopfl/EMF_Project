using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class
    VeteransClaimsSqliteServiceConnectionRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsTheoriesAndBases()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var claimIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(
                databasePath)
                .AddClaimIssueAsync(claimIssue);

            var theory = new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-001"),
                ClaimIssueId = claimIssue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Secondary
            };

            var repository =
                new SqliteServiceConnectionRepository(
                    databasePath);

            await repository
                .AddServiceConnectionTheoryAsync(
                    theory);

            var stored =
                await repository
                    .GetServiceConnectionTheoryAsync(
                        theory.Id);

            var issueTheories =
                await repository
                    .GetServiceConnectionTheoriesAsync(
                        claimIssue.Id);

            Assert.NotNull(stored);
            Assert.Equal(theory.Id, stored!.Id);
            Assert.Equal(
                theory.ClaimIssueId,
                stored.ClaimIssueId);
            Assert.Equal(
                theory.TheoryType,
                stored.TheoryType);

            Assert.Equal(
                theory.Id,
                Assert.Single(issueTheories).Id);

            var basis = new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-001"),
                ClaimIssueId = claimIssue.Id,
                ServiceConnectionTheoryId =
                    theory.Id
            };

            await repository
                .AddServiceConnectionBasisAsync(
                    basis);

            var storedBasis =
                await repository
                    .GetServiceConnectionBasisAsync(
                        basis.Id);

            var issueBases =
                await repository
                    .GetServiceConnectionBasesAsync(
                        claimIssue.Id);

            var theoryBases =
                await repository
                    .GetServiceConnectionBasesAsync(
                        theory.Id);

            Assert.NotNull(storedBasis);
            Assert.Equal(
                basis.Id,
                storedBasis!.Id);
            Assert.Equal(
                basis.ClaimIssueId,
                storedBasis.ClaimIssueId);
            Assert.Equal(
                basis.ServiceConnectionTheoryId,
                storedBasis.ServiceConnectionTheoryId);

            Assert.Equal(
                basis.Id,
                Assert.Single(issueBases).Id);

            Assert.Equal(
                basis.Id,
                Assert.Single(theoryBases).Id);

            var claimedCondition =
                new ClaimedCondition
                {
                    Id =
                        new ClaimedConditionId(
                            "claimed-condition-001"),
                    ClaimIssueId = claimIssue.Id,
                    Name = "Sleep apnea"
                };

            await new SqliteConditionRepository(
                databasePath)
                .AddClaimedConditionAsync(
                    claimedCondition);

            await repository
                .AddBasisClaimedConditionAsync(
                    new ServiceConnectionBasisClaimedCondition
                    {
                        ServiceConnectionBasisId =
                            basis.Id,
                        ClaimedConditionId =
                            claimedCondition.Id
                    });

            var basisClaimedConditionIds =
                await repository
                    .GetClaimedConditionIdsAsync(
                        basis.Id);

            var claimedConditionBasisIds =
                await repository
                    .GetServiceConnectionBasisIdsAsync(
                        claimedCondition.Id);

            Assert.Equal(
                claimedCondition.Id,
                Assert.Single(
                    basisClaimedConditionIds));

            Assert.Equal(
                basis.Id,
                Assert.Single(
                    claimedConditionBasisIds));

            var serviceEvent =
                new ServiceEvent
                {
                    Id =
                        new ServiceEventId(
                            "service-event-001"),
                    VeteranId = veteran.Id,
                    Description =
                        "Documented duty event"
                };

            await new SqliteServiceHistoryRepository(
                databasePath)
                .AddServiceEventAsync(serviceEvent);

            await repository
                .AddBasisServiceEventAsync(
                    new ServiceConnectionBasisServiceEvent
                    {
                        ServiceConnectionBasisId =
                            basis.Id,
                        ServiceEventId =
                            serviceEvent.Id
                    });

            var basisServiceEventIds =
                await repository
                    .GetServiceEventIdsAsync(
                        basis.Id);

            var serviceEventBasisIds =
                await repository
                    .GetServiceConnectionBasisIdsAsync(
                        serviceEvent.Id);

            Assert.Equal(
                serviceEvent.Id,
                Assert.Single(
                    basisServiceEventIds));

            Assert.Equal(
                basis.Id,
                Assert.Single(
                    serviceEventBasisIds));


            var exposure =
                new Exposure
                {
                    Id =
                        new ExposureId(
                            "exposure-001"),
                    VeteranId = veteran.Id,
                    ExposureType =
                        "Environmental"
                };

            await new SqliteServiceHistoryRepository(
                databasePath)
                .AddExposureAsync(exposure);

            await repository
                .AddBasisExposureAsync(
                    new ServiceConnectionBasisExposure
                    {
                        ServiceConnectionBasisId =
                            basis.Id,
                        ExposureId =
                            exposure.Id
                    });

            var basisExposureIds =
                await repository
                    .GetExposureIdsAsync(
                        basis.Id);

            var exposureBasisIds =
                await repository
                    .GetServiceConnectionBasisIdsAsync(
                        exposure.Id);

            Assert.Equal(
                exposure.Id,
                Assert.Single(
                    basisExposureIds));

            Assert.Equal(
                basis.Id,
                Assert.Single(
                    exposureBasisIds));

            var conditionRepository =
                new SqliteConditionRepository(
                    databasePath);

            var serviceConnectedCondition =
                new MedicalCondition
                {
                    Id =
                        new MedicalConditionId(
                            "medical-condition-001"),
                    Name = "Posttraumatic stress disorder"
                };

            await conditionRepository
                .AddMedicalConditionAsync(
                    serviceConnectedCondition);

            await conditionRepository
                .AddVeteranMedicalConditionAsync(
                    new VeteranMedicalCondition
                    {
                        VeteranId = veteran.Id,
                        MedicalConditionId =
                            serviceConnectedCondition.Id
                    });

            await repository
                .AddBasisServiceConnectedConditionAsync(
                    new ServiceConnectionBasisServiceConnectedCondition
                    {
                        ServiceConnectionBasisId =
                            basis.Id,
                        ServiceConnectedConditionId =
                            serviceConnectedCondition.Id
                    });

            var basisConditionIds =
                await repository
                    .GetServiceConnectedConditionIdsAsync(
                        basis.Id);

            var conditionBasisIds =
                await repository
                    .GetServiceConnectionBasisIdsAsync(
                        serviceConnectedCondition.Id);

            Assert.Equal(
                serviceConnectedCondition.Id,
                Assert.Single(
                    basisConditionIds));

            Assert.Equal(
                basis.Id,
                Assert.Single(
                    conditionBasisIds));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
    [Fact]
    public async Task
        Repository_RejectsTheoryForMissingClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteServiceConnectionRepository(
                    databasePath);

            await repository.InitializeAsync();

            var theory = new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-001"),
                ClaimIssueId =
                    new ClaimIssueId(
                        "missing-claim-issue"),
                TheoryType =
                    ServiceConnectionTheoryTypes.Direct
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddServiceConnectionTheoryAsync(
                        theory));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task
        Repository_RejectsBasisForDifferentClaimIssue()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(
                databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-001"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var firstIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-001"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var secondIssue = new ClaimIssue
            {
                Id =
                    new ClaimIssueId(
                        "claim-issue-002"),
                ClaimId = claim.Id,
                ClaimIssueType =
                    ClaimIssueTypes.ServiceConnection
            };

            var issueRepository =
                new SqliteClaimIssueRepository(
                    databasePath);

            await issueRepository.AddClaimIssueAsync(
                firstIssue);

            await issueRepository.AddClaimIssueAsync(
                secondIssue);

            var repository =
                new SqliteServiceConnectionRepository(
                    databasePath);

            var theory = new ServiceConnectionTheory
            {
                Id =
                    new ServiceConnectionTheoryId(
                        "theory-001"),
                ClaimIssueId = firstIssue.Id,
                TheoryType =
                    ServiceConnectionTheoryTypes.Direct
            };

            await repository
                .AddServiceConnectionTheoryAsync(
                    theory);

            var basis = new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-001"),
                ClaimIssueId = secondIssue.Id,
                ServiceConnectionTheoryId =
                    theory.Id
            };

            await Assert.ThrowsAsync<SqliteException>(
                () => repository
                    .AddServiceConnectionBasisAsync(
                        basis));

            var validBasis = new ServiceConnectionBasis
            {
                Id =
                    new ServiceConnectionBasisId(
                        "basis-002"),
                ClaimIssueId = firstIssue.Id,
                ServiceConnectionTheoryId =
                    theory.Id
            };

            await repository
                .AddServiceConnectionBasisAsync(
                    validBasis);

            var claimedCondition =
                new ClaimedCondition
                {
                    Id =
                        new ClaimedConditionId(
                            "claimed-condition-001"),
                    ClaimIssueId = secondIssue.Id,
                    Name = "Different issue condition"
                };

            await new SqliteConditionRepository(
                databasePath)
                .AddClaimedConditionAsync(
                    claimedCondition);

            var association =
                new ServiceConnectionBasisClaimedCondition
                {
                    ServiceConnectionBasisId =
                        validBasis.Id,
                    ClaimedConditionId =
                        claimedCondition.Id
                };

            var exception =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => repository
                            .AddBasisClaimedConditionAsync(
                                association));

            Assert.Contains(
                "belong to the same claim issue",
                exception.Message);


            var secondVeteran =
                new Veteran
                {
                    Id =
                        new VeteranId(
                            "veteran-002")
                };

            await new SqliteVeteranRepository(
                databasePath)
                .AddVeteranAsync(secondVeteran);

            var otherVeteranServiceEvent =
                new ServiceEvent
                {
                    Id =
                        new ServiceEventId(
                            "service-event-002"),
                    VeteranId = secondVeteran.Id,
                    Description =
                        "Event belonging to another veteran"
                };

            await new SqliteServiceHistoryRepository(
                databasePath)
                .AddServiceEventAsync(
                    otherVeteranServiceEvent);

            var serviceEventAssociation =
                new ServiceConnectionBasisServiceEvent
                {
                    ServiceConnectionBasisId =
                        validBasis.Id,
                    ServiceEventId =
                        otherVeteranServiceEvent.Id
                };

            var serviceEventException =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => repository
                            .AddBasisServiceEventAsync(
                                serviceEventAssociation));

            Assert.Contains(
                "belong to the same veteran",
                serviceEventException.Message);


            var otherVeteranExposure =
                new Exposure
                {
                    Id =
                        new ExposureId(
                            "exposure-002"),
                    VeteranId = secondVeteran.Id,
                    ExposureType =
                        "Environmental"
                };

            await new SqliteServiceHistoryRepository(
                databasePath)
                .AddExposureAsync(
                    otherVeteranExposure);

            var exposureAssociation =
                new ServiceConnectionBasisExposure
                {
                    ServiceConnectionBasisId =
                        validBasis.Id,
                    ExposureId =
                        otherVeteranExposure.Id
                };

            var exposureException =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => repository
                            .AddBasisExposureAsync(
                                exposureAssociation));

            Assert.Contains(
                "belong to the same veteran",
                exposureException.Message);

            var otherConditionRepository =
                new SqliteConditionRepository(
                    databasePath);

            var otherVeteranCondition =
                new MedicalCondition
                {
                    Id =
                        new MedicalConditionId(
                            "medical-condition-002"),
                    Name = "Other veteran condition"
                };

            await otherConditionRepository
                .AddMedicalConditionAsync(
                    otherVeteranCondition);

            await otherConditionRepository
                .AddVeteranMedicalConditionAsync(
                    new VeteranMedicalCondition
                    {
                        VeteranId = secondVeteran.Id,
                        MedicalConditionId =
                            otherVeteranCondition.Id
                    });

            var conditionAssociation =
                new ServiceConnectionBasisServiceConnectedCondition
                {
                    ServiceConnectionBasisId =
                        validBasis.Id,
                    ServiceConnectedConditionId =
                        otherVeteranCondition.Id
                };

            var conditionException =
                await Assert.ThrowsAsync<
                    InvalidOperationException>(
                        () => repository
                            .AddBasisServiceConnectedConditionAsync(
                                conditionAssociation));

            Assert.Contains(
                "belong to the same veteran",
                conditionException.Message);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

}
