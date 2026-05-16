// Minimal TronWeb type surface used by the sidecar (T70+).
// tronweb 5.x ships without TypeScript declarations and there is no
// @types/tronweb package on npm. The runtime exports we rely on are stable
// across the 5.x line, so this hand-written declaration is sufficient.
declare module 'tronweb' {
  namespace TronWeb {
    interface AddressUtility {
      fromPrivateKey(privateKey: string): string;
      toHex(address: string): string;
      fromHex(addressHex: string): string;
    }
    interface Utils {
      isHex(value: unknown): boolean;
    }
  }

  interface TronWeb {
    address: TronWeb.AddressUtility;
  }

  const TronWeb: {
    new (options: {
      fullHost?: string;
      fullNode?: string;
      solidityNode?: string;
      eventServer?: string;
      headers?: Record<string, string>;
      privateKey?: string;
    }): TronWeb;
    address: TronWeb.AddressUtility;
    utils: TronWeb.Utils;
  };

  export = TronWeb;
}
