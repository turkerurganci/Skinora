import { describe, it, expect } from 'vitest';
import {
  CommunityHealthTracker,
  COMMUNITY_FAILURE_WINDOW_MS,
  COMMUNITY_UNHEALTHY_AFTER_FAILURES,
} from './CommunityHealth.js';

describe('CommunityHealthTracker (08 §2.2a)', () => {
  it('sessiz sidecar SAĞLIKLIDIR — "kimse sormuyor" arıza değildir', () => {
    // Kalıcı olarak sağlıksız kalan bir sidecar her Steam-bağlı timeout'u
    // sonsuza kadar dondurur; T133'ün sildiği arıza tam olarak buydu.
    expect(new CommunityHealthTracker().isUnhealthy()).toBe(false);
  });

  it('tek tük hata arıza sayılmaz', () => {
    const sut = new CommunityHealthTracker(() => 1_000);
    for (let i = 0; i < COMMUNITY_UNHEALTHY_AFTER_FAILURES - 1; i++) sut.recordFailure();
    expect(sut.isUnhealthy()).toBe(false);
  });

  it('üst üste yeterli hata arızadır', () => {
    const sut = new CommunityHealthTracker(() => 1_000);
    for (let i = 0; i < COMMUNITY_UNHEALTHY_AFTER_FAILURES; i++) sut.recordFailure();
    expect(sut.isUnhealthy()).toBe(true);
  });

  it('araya giren tek başarı seriyi sıfırlar', () => {
    const sut = new CommunityHealthTracker(() => 1_000);
    sut.recordFailure();
    sut.recordFailure();
    sut.recordSuccess();
    sut.recordFailure();
    expect(sut.isUnhealthy()).toBe(false);
    expect(sut.snapshot().consecutiveFailures).toBe(1);
  });

  it('pencere geçince sağlıklıya döner — sessizlik "iyileşti" değil "bilmiyoruz"', () => {
    let clock = 1_000;
    const sut = new CommunityHealthTracker(() => clock);
    for (let i = 0; i < COMMUNITY_UNHEALTHY_AFTER_FAILURES; i++) sut.recordFailure();
    expect(sut.isUnhealthy()).toBe(true);

    clock += COMMUNITY_FAILURE_WINDOW_MS + 1;
    expect(sut.isUnhealthy()).toBe(false);
  });
});
