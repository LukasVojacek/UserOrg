// Lints the MR title (the squash commit that lands on main) against conventional-commits.
// Type list mirrors .releaserc.js releaseRules (adds `dep`/`other`, no `style`) — keep in sync.
// Types are enforced lowercase (the conventional-commits standard): stricter than .releaserc.js's
// permissive case handling, but safe — any lint-clean title is always accepted by the release config.
module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [
      2,
      'always',
      ['build', 'chore', 'ci', 'dep', 'docs', 'feat', 'fix', 'perf', 'refactor', 'revert', 'test', 'other'],
    ],
    'subject-case': [0], // titles carry proper nouns (e.g. "HEAppE")
  },
};
