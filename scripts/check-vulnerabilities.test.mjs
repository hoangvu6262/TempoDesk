import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import { checkVulnerabilities } from './check-vulnerabilities.mjs';

const report = (severity, transitive = false) => ({
  version: 1,
  projects: [{ path: 'test.csproj', frameworks: [{ framework: 'net10.0',
    [transitive ? 'transitivePackages' : 'topLevelPackages']: [{ id: 'Example', resolvedVersion: '1.0.0',
      vulnerabilities: [{ severity, advisoryurl: 'https://example.com/advisory' }] }],
  }] }],
});

test('allows clean, low, and moderate reports', () => {
  assert.deepEqual(checkVulnerabilities({ version: 1, projects: [{ path: 'clean.csproj' }] }), []);
  assert.deepEqual(checkVulnerabilities(report('Low')), []);
  assert.deepEqual(checkVulnerabilities(report('Moderate')), []);
});

test('rejects direct high and transitive critical vulnerabilities', () => {
  assert.match(checkVulnerabilities(report('High'))[0], /Example 1.0.0: High/);
  assert.match(checkVulnerabilities(report('Critical', true))[0], /Example 1.0.0: Critical/);
});

test('fails closed on tool errors, unsupported reports, and malformed severities', () => {
  for (const value of [null, {}, { version: 2, projects: [] }, { version: 1, projects: [] },
    { version: 1, projects: [{}] },
    { version: 1, projects: [{ path: 'broken.csproj', frameworks: 'invalid' }] },
    { version: 1, projects: [{ path: 'broken.csproj', frameworks: ['invalid'] }] },
    { ...report('Low'), problems: {} },
    { ...report('Low'), problems: [{ severity: 'error', text: 'Restore failed' }] },
    { ...report('Low'), problems: [{ severity: 'warning', text: 'Audit source unavailable' }] },
    report('Unknown')]) {
    assert.throws(() => checkVulnerabilities(value));
  }
});

test('CLI exits unsuccessfully for malformed JSON and high vulnerabilities', () => {
  for (const input of ['not json', JSON.stringify(report('High'))]) {
    const result = spawnSync(process.execPath, ['scripts/check-vulnerabilities.mjs', '/dev/stdin'], { input });
    assert.equal(result.status, 1);
  }
  const clean = spawnSync(process.execPath, ['scripts/check-vulnerabilities.mjs', '/dev/stdin'], {
    input: JSON.stringify(report('Low')),
  });
  assert.equal(clean.status, 0);
});
