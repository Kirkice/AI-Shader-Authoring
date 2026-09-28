import test from 'node:test';
import assert from 'node:assert/strict';
import { constantTimeTextEquals, createPairingProof, FixedWindowRateLimiter, isLoopbackHost, ReplayCache } from '../build/security.js';

test('loopback policy accepts only local endpoints', () => {
  assert.equal(isLoopbackHost('127.0.0.1'), true);
  assert.equal(isLoopbackHost('localhost'), true);
  assert.equal(isLoopbackHost('[::1]'), true);
  assert.equal(isLoopbackHost('0.0.0.0'), false);
  assert.equal(isLoopbackHost('192.168.1.10'), false);
});

test('pairing proof is deterministic and rejects changed binding fields', () => {
  const proof = createPairingProof('0123456789abcdef', 'challenge', 'epoch', 'editor', 'h:/project');
  assert.equal(constantTimeTextEquals(proof, createPairingProof('0123456789abcdef', 'challenge', 'epoch', 'editor', 'h:/project')), true);
  assert.equal(constantTimeTextEquals(proof, createPairingProof('0123456789abcdef', 'challenge', 'old', 'editor', 'h:/project')), false);
});

test('replay cache rejects duplicates and evicts oldest values', () => {
  const cache = new ReplayCache(2);
  assert.equal(cache.accept('a'), true);
  assert.equal(cache.accept('a'), false);
  assert.equal(cache.accept('b'), true);
  assert.equal(cache.accept('c'), true);
  assert.equal(cache.accept('a'), true);
});

test('fixed window limiter enforces and resets its budget', () => {
  const limiter = new FixedWindowRateLimiter(2, 1000);
  assert.equal(limiter.allow(1000), true);
  assert.equal(limiter.allow(1001), true);
  assert.equal(limiter.allow(1002), false);
  assert.equal(limiter.allow(2000), true);
});
