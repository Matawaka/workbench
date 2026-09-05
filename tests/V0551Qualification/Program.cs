using Matawaka.Workbench.App;

var checks = RealHostModelInvocationAdmissionVerifierV0551.RunOfflineContractChecks()
    .Select(x => ($"admission:{x.Id}", x.Passed, x.Observed, x.Expected))
    .Concat(FixedGitHubPublicationV0551Service.RunOfflineContractChecks()
        .Select(x => ($"publication:{x.Id}", x.Passed, x.Observed, x.Expected)))
    .Concat(LocalCheckpointV0551Service.RunOfflineContractChecks()
        .Select(x => ($"checkpoint:{x.Id}", x.Passed, x.Observed, x.Expected)))
    .ToArray();

foreach (var check in checks)
    Console.WriteLine($"{check.Item1}: {(check.Passed ? "PASS" : "FAIL")} observed={check.Observed} expected={check.Expected}");

var failed = checks.Where(x => !x.Passed).ToArray();
if (failed.Length != 0)
{
    Console.Error.WriteLine($"V0551_PUBLICATION_CLOSURE_QUALIFICATION_FAILED count={failed.Length}");
    return 1;
}

Console.WriteLine($"V0551_PUBLICATION_CLOSURE_QUALIFICATION_PASS checks={checks.Length}");
return 0;
