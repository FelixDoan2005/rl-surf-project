"""Lets the tests import envs/, algos/ and the scripts the same way the scripts do."""
import os
import sys

AGENT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, AGENT)
sys.path.insert(0, os.path.join(AGENT, "scripts"))
