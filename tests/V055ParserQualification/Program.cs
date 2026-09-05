using Matawaka.Workbench.App;

var failures = new List<string>();
foreach (var check in LocalModelInvocationRequestV055Parser.RunOfflineContractChecks())
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Id}: observed={check.Observed}; expected={check.Expected}");
    if (!check.Passed) failures.Add(check.Id);
}

if (failures.Count != 0)
{
    Console.Error.WriteLine("V055 exact request parser qualification failed: " + string.Join(", ", failures));
    return 1;
}

Console.WriteLine("V055_EXACT_REQUEST_PARSER_PASS");
return 0;
