import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export function checkVulnerabilities(report) {
  assert.equal(report?.version, 1, 'Unsupported NuGet vulnerability report');
  assert.ok(Array.isArray(report.projects) && report.projects.length > 0, 'NuGet report has no projects');
  assert.ok(report.problems === undefined || Array.isArray(report.problems), 'Malformed NuGet problems');
  assert.ok(!report.problems?.length, 'NuGet reported audit/restore problems');
  const failures = [];
  for (const project of report.projects) {
    assert.ok(typeof project.path === 'string' && project.path.length > 0, 'NuGet project has no path');
    const frameworks = project.frameworks ?? [];
    assert.ok(Array.isArray(frameworks), `Malformed frameworks: ${project.path}`);
    for (const framework of frameworks) {
      assert.ok(framework && typeof framework === 'object' && !Array.isArray(framework), 'Malformed framework');
      for (const kind of ['topLevelPackages', 'transitivePackages']) {
        const packages = framework[kind] ?? [];
        assert.ok(Array.isArray(packages), `Malformed ${kind}`);
        for (const dependency of packages) {
          const vulnerabilities = dependency.vulnerabilities ?? [];
          assert.ok(Array.isArray(vulnerabilities), 'Malformed vulnerability list');
          for (const vulnerability of vulnerabilities) {
            assert.ok(['Low', 'Moderate', 'High', 'Critical'].includes(vulnerability.severity), 'Unknown vulnerability severity');
            if (['High', 'Critical'].includes(vulnerability.severity)) {
              failures.push(`${project.path}: ${dependency.id} ${dependency.resolvedVersion}: ${vulnerability.severity} ${vulnerability.advisoryurl}`);
            }
          }
        }
      }
    }
  }
  return failures;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const failures = checkVulnerabilities(JSON.parse(readFileSync(process.argv[2], 'utf8')));
  if (failures.length) {
    console.error(failures.join('\n'));
    process.exitCode = 1;
  } else {
    console.log('No High or Critical NuGet vulnerabilities found.');
  }
}
