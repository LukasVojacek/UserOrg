// Type B hotfix: a temporary support/vX.Y.x branch cut from an old release tag ships a patch of
// that line while main has already moved on. semantic-release treats a branch as `maintenance`
// only when its name IS a semver range (2.3.x) or it carries an explicit `range`, so the range is
// derived here from the branch the pipeline runs on. Nothing is added on any other branch.
const supportBranch = process.env.CI_COMMIT_BRANCH || '';
const supportMatch = /^support\/v?(\d+\.\d+)\.(?:x|\d+)$/.exec(supportBranch);

module.exports = {
  branches: [
    { name: 'main', channel: 'latest', prerelease: false },
    ...(supportMatch
      ? [{ name: supportBranch, range: `${supportMatch[1]}.x`, channel: `${supportMatch[1]}.x` }]
      : []),
    // Single-main trunk model: every non-main branch resolves to the 'rc' prerelease channel.
    // semantic-release is only ever invoked here with --dry-run (version-preview), so this NEVER
    // creates rc tags — it only lets the projection compute a sensible next version off a branch.
    { name: '/^(?!main$).*/', prerelease: 'rc', channel: 'rc' }
  ],
  plugins: [
    [
      '@semantic-release/commit-analyzer',
      {
        preset: 'conventionalcommits',
        releaseRules: [
          { breaking: true, release: 'major' },

          { type: 'build',    release: 'patch' },
          { type: 'Build',    release: 'patch' },
          { type: 'BUILD',    release: 'patch' },

          { type: 'chore',    release: false },
          { type: 'Chore',    release: false },
          { type: 'CHORE',    release: false },

          { type: 'ci',       release: false },
          { type: 'Ci',       release: false },
          { type: 'CI',       release: false },

          { type: 'dep',      release: 'patch' },
          { type: 'Dep',      release: 'patch' },
          { type: 'DEP',      release: 'patch' },

          { type: 'docs',     release: false },
          { type: 'Docs',     release: false },
          { type: 'DOCS',     release: false },

          { type: 'feat',     release: 'minor' },
          { type: 'Feat',     release: 'minor' },
          { type: 'FEAT',     release: 'minor' },

          { type: 'fix',      release: 'patch' },
          { type: 'Fix',      release: 'patch' },
          { type: 'FIX',      release: 'patch' },

          { type: 'perf',     release: 'patch' },
          { type: 'Perf',     release: 'patch' },
          { type: 'PERF',     release: 'patch' },

          { type: 'refactor', release: false },
          { type: 'Refactor', release: false },
          { type: 'REFACTOR', release: false },

          { type: 'revert',   release: 'patch' },
          { type: 'Revert',   release: 'patch' },
          { type: 'REVERT',   release: 'patch' },

          { type: 'test',     release: false },
          { type: 'Test',     release: false },
          { type: 'TEST',     release: false },

          { type: 'other',    release: false },
          { type: 'Other',    release: false },
          { type: 'OTHER',    release: false }
        ]
      }
    ],
    [
      '@semantic-release/release-notes-generator',
      {
        preset: 'conventionalcommits',
        writerOpts: {
          groupBy: 'type',
          commitGroupsSort: 'title',
          commitsSort: 'header'
        },
        presetConfig: {
          issuePrefixes: ['#'],
          referencesActions: ['close', 'closes', 'fix', 'fixes', 'resolve', 'resolves'],
          issueUrlFormat: process.env.CI_SERVER_URL + '/' + process.env.CI_PROJECT_PATH + '/-/issues/{{id}}',
          types: [
            { type: 'build',    section: '**👷 Build**' },
            { type: 'Build',    section: '**👷 Build**' },
            { type: 'BUILD',    section: '**👷 Build**' },

            { type: 'chore',    section: '**🧹 Chores**' },
            { type: 'Chore',    section: '**🧹 Chores**' },
            { type: 'CHORE',    section: '**🧹 Chores**' },

            { type: 'ci',       section: '**🚦 CI/CD**' },
            { type: 'Ci',       section: '**🚦 CI/CD**' },
            { type: 'CI',       section: '**🚦 CI/CD**' },

            { type: 'dep',      section: '**👾 Dependencies**' },
            { type: 'Dep',      section: '**👾 Dependencies**' },
            { type: 'DEP',      section: '**👾 Dependencies**' },

            { type: 'docs',     section: '**📚 Docs**' },
            { type: 'Docs',     section: '**📚 Docs**' },
            { type: 'DOCS',     section: '**📚 Docs**' },

            { type: 'feat',     section: '**🚀 Features**' },
            { type: 'Feat',     section: '**🚀 Features**' },
            { type: 'FEAT',     section: '**🚀 Features**' },

            { type: 'fix',      section: '**🛠️ Fixes**' },
            { type: 'Fix',      section: '**🛠️ Fixes**' },
            { type: 'FIX',      section: '**🛠️ Fixes**' },

            { type: 'perf',     section: '**⏩ Performance**' },
            { type: 'Perf',     section: '**⏩ Performance**' },
            { type: 'PERF',     section: '**⏩ Performance**' },

            { type: 'refactor', section: '**🔨 Refactor**' },
            { type: 'Refactor', section: '**🔨 Refactor**' },
            { type: 'REFACTOR', section: '**🔨 Refactor**' },

            { type: 'revert',   section: '**🙅‍♂️ Reverts**' },
            { type: 'Revert',   section: '**🙅‍♂️ Reverts**' },
            { type: 'REVERT',   section: '**🙅‍♂️ Reverts**' },

            { type: 'test',     section: '**🚥 Tests**' },
            { type: 'Test',     section: '**🚥 Tests**' },
            { type: 'TEST',     section: '**🚥 Tests**' },

            { type: 'other',    section: '**Other**' },
            { type: 'Other',    section: '**Other**' },
            { type: 'OTHER',    section: '**Other**' }
          ]
        }
      }
    ],
    // GitLab Release — created ONLY for the manual PRIMARY release (pipeline run with RELEASE=true).
    // Changes are recorded solely as GitLab Release notes; there is no CHANGELOG.md commit-back.
    // The per-merge rc projection (version-preview --dry-run) does not load this plugin, so it
    // needs no token and tags nothing.
    ...(process.env.RELEASE === 'true'
      ? [
          [
            '@semantic-release/gitlab',
            {
              gitlabUrl: process.env.CI_SERVER_URL || 'https://gitlab.com',
              assets: [
                {
                  path: 'migrations/sql/migration.sql',
                  label: 'Migration SQL Script',
                  type: 'other'
                },
                {
                  label: 'Container Image',
                  type: 'other',
                  url: 'https://opencode.it4i.eu:5050/lexis-platform/backend/userorg:${nextRelease.version}'
                }
              ]
            }
          ]
        ]
      : []),
    [
      '@semantic-release/exec',
      {
        prepareCmd: "echo 'Simulating build process'",
        publishCmd: "echo 'Simulating publish' && echo VERSION=${nextRelease.version} >> release.env && echo VERSION_CHANGED=true >> release.env && echo RELEASE_TYPE=${nextRelease.releaseType} >> release.env",
        failCmd: "echo VERSION=${lastRelease.version} >> release.env && echo VERSION_CHANGED=false >> release.env && echo RELEASE_TYPE=${nextRelease.releaseType:-none} >> release.env"
      }
    ]
  ]
};
