declare module 'sm-crypto' {
  export interface Sm2KeyPair {
    privateKey: string
    publicKey: string
  }

  export interface Sm2SignOptions {
    pointPool?: unknown
    der?: boolean
    hash?: boolean
    publicKey?: string
  }

  export const sm2: {
    generateKeyPairHex(): Sm2KeyPair
    doEncrypt(msg: string, publicKey: string, cipherMode?: 0 | 1): string
    doDecrypt(encryptData: string, privateKey: string, cipherMode?: 0 | 1): string
    doSignature(msg: string, privateKey: string, options?: Sm2SignOptions): string
    doVerifySignature(msg: string, signHex: string, publicKey: string, options?: Sm2SignOptions): boolean
    getPublicKeyFromPrivateKey(privateKey: string): string
    verifyPublicKey(publicKey: string): boolean
  }

  export const sm3: (msg: string | number[]) => string

  export const sm4: {
    encrypt(msg: string, key: string, options?: { mode?: 'cbc' | 'ecb'; iv?: string }): string
    decrypt(hex: string, key: string, options?: { mode?: 'cbc' | 'ecb'; iv?: string }): string
  }
}
