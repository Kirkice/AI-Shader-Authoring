import { createHmac, timingSafeEqual } from 'node:crypto';

export function isLoopbackHost(host: string): boolean {
  const normalized = host.trim().toLowerCase().replace(/^\[|\]$/g, '');
  return normalized === '127.0.0.1' || normalized === 'localhost' || normalized === '::1';
}

export function createPairingProof(secret: string, challenge: string, epoch: string, editorId: string, projectPath: string): string {
  return createHmac('sha256', secret).update(`${challenge}:${epoch}:${editorId}:${projectPath}`).digest('hex');
}

export function constantTimeTextEquals(actual: string, expected: string): boolean {
  return actual.length === expected.length
    && timingSafeEqual(Buffer.from(actual, 'utf8'), Buffer.from(expected, 'utf8'));
}

export class FixedWindowRateLimiter {
  private windowStartedAt = 0;
  private count = 0;
  constructor(private readonly limit: number, private readonly windowMs: number) {}

  allow(now = Date.now()): boolean {
    if (now - this.windowStartedAt >= this.windowMs) {
      this.windowStartedAt = now;
      this.count = 0;
    }
    this.count++;
    return this.count <= this.limit;
  }
}

export class ReplayCache {
  private readonly values = new Set<string>();
  private readonly order: string[] = [];
  constructor(private readonly capacity: number) {}

  accept(value: string): boolean {
    if (!value || this.values.has(value)) return false;
    this.values.add(value);
    this.order.push(value);
    while (this.order.length > this.capacity) this.values.delete(this.order.shift()!);
    return true;
  }
}
