export const Component = (
  metadata: Readonly<{ selector: string; template: string }>,
): ClassDecorator => (target) => {
  Object.defineProperty(target, "signalDeskComponent", {
    value: metadata,
    enumerable: false,
  });
};
