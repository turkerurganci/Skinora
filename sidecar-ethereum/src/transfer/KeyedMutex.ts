/**
 * One-at-a-time execution per key (T152). Every send and every keeper step of
 * one sender runs under that sender's key, so the nonce a send reads and the
 * journal record it writes cannot interleave with another send of the same
 * account — the "nonce is managed in one place" rule (08 §3.7.6) inside one
 * process. The service runs as a single container (05 §8.1).
 */
export class KeyedMutex {
  private readonly tails = new Map<string, Promise<unknown>>();

  run<T>(key: string, task: () => Promise<T>): Promise<T> {
    const previous = this.tails.get(key) ?? Promise.resolve();
    const result = previous.then(task, task);
    const tail = result.then(
      () => undefined,
      () => undefined,
    );
    this.tails.set(key, tail);
    void tail.then(() => {
      if (this.tails.get(key) === tail) this.tails.delete(key);
    });
    return result;
  }
}
