import unittest

from changes import REPOSITORY, classify, eligible


class ChangeTests(unittest.TestCase):
    def test_client_server_shared_docs_and_dependencies(self):
        fixtures = [(['src/Client/App.razor'], (True, False)), (['src/Server/API/Program.cs'], (False, True)),
            (['src/ChefKnifeStudios.PokerAttack.Shared/DTO.cs'], (True, True)), (['docs/operations.md'], (False, False)),
            (['README.md'], (False, False)), (['.dockerignore'], (True, True)), (['tests/package-lock.json'], (True, True)),
            (['tests/e2e/migration-smoke.spec.ts'], (True, True)), (['Directory.Build.props'], (True, True)),
            (['deploy/prepare-production-client.test.mjs'], (True, False)), (['deploy/linode/release_evidence.py'], (False, True)),
            (['new-build-input.config'], (True, True))]
        for paths, expected in fixtures:
            with self.subTest(paths=paths): self.assertEqual(tuple(classify(paths).values()), expected)

    def test_only_original_repository_main_push_or_dispatch_can_request_production(self):
        for event in ('pull_request', 'push', 'workflow_dispatch'):
            for ref in ('refs/heads/main', 'refs/heads/migration/linode-server-hosting', 'refs/pull/12/merge'):
                for repo in (REPOSITORY, 'fork/ChefKnifeStudios.PokerAttack'):
                    with self.subTest(event=event, ref=ref, repo=repo):
                        self.assertEqual(eligible(event, ref, repo), event != 'pull_request' and ref == 'refs/heads/main' and repo == REPOSITORY)


if __name__ == '__main__': unittest.main(verbosity=2)
