declare module "*.svg" {
  const src: string;
  export default src;
}

declare module "*.module.scss" {
  const classes: { readonly [key: string]: string };
  export default classes;
}

declare module "mod.json" {
  const mod: { id: string; name: string; version: string; author: string };
  export default mod;
}

declare module "cs2/modding" {
  export type ModRegistrar = (moduleRegistry: any) => void;
  export function getModule(path: string, exportName: string): any;
}

declare module "cs2/api" {
  export function bindValue<T>(group: string, name: string, fallback?: T): any;
  export function useValue<T>(binding: any): T;
  export function trigger(group: string, name: string, ...args: any[]): void;
}

declare module "cs2/ui" {
  export const Button: any;
  export const FloatingButton: any;
}
