"""Manual delivery uses the governed workflow and its durable host lease.

The transitional root-bound bootstrap publisher is handled separately. Arbitrary
local directories and unleased maintenance uploads are not accepted here.
"""
from pathlib import Path
import sys

sys.path.insert(0,str(Path(__file__).resolve().parent/'release'))
from manual_client import cli

if __name__=='__main__':cli()
