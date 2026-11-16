"""A working-tree test must not hide inputs that Git would omit from CI."""
from pathlib import Path
import subprocess
import unittest


class SourceInputsTests(unittest.TestCase):
    def test_release_source_directories_are_not_ignored_as_build_output(self):
        root=Path(__file__).parents[2]
        required=[*root.joinpath('deploy/release').glob('*.py'),*root.joinpath('tests/release').glob('*.cjs'),
            root/'src/ChefKnifeStudios.PokerAttack.Shared/DTOs/Release/OwnedReleaseSessionsDTO.cs',
            root/'deploy/monitoring/linode-docker-dashboard.json']
        self.assertTrue(all(path.is_file() for path in required))
        result=subprocess.run(['git','check-ignore','--no-index',*[path.relative_to(root).as_posix() for path in required]],
            cwd=root,capture_output=True)
        self.assertEqual(result.returncode,1,result.stdout.decode(errors='replace'))


if __name__=='__main__':unittest.main(verbosity=2)
