import { cn } from "@/lib/utils/cn";

export interface SkeletonProps {
  className?: string;
}

export function Skeleton({ className }: SkeletonProps) {
  return (
    <div
      className={cn("animate-pulse rounded-md bg-gray-200", className)}
      role="status"
      aria-busy="true"
    />
  );
}

export interface SpinnerProps {
  size?: "sm" | "md" | "lg";
  className?: string;
  label?: string;
}

const SIZE_MAP: Record<NonNullable<SpinnerProps["size"]>, string> = {
  sm: "h-4 w-4 border-2",
  md: "h-6 w-6 border-2",
  lg: "h-10 w-10 border-4",
};

export function Spinner({ size = "md", className, label }: SpinnerProps) {
  return (
    <span
      className={cn(
        "inline-block animate-spin rounded-full border-gray-300 border-t-blue-600",
        SIZE_MAP[size],
        className,
      )}
      role="status"
      aria-label={label ?? "Loading"}
    />
  );
}

export interface ProgressProps {
  value: number;
  max?: number;
  className?: string;
  label?: string;
}

export function Progress({ value, max = 100, className, label }: ProgressProps) {
  const pct = Math.min(100, Math.max(0, (value / max) * 100));
  return (
    <div
      className={cn("w-full overflow-hidden rounded-full bg-gray-200", className)}
      role="progressbar"
      aria-valuenow={value}
      aria-valuemin={0}
      aria-valuemax={max}
      aria-label={label}
    >
      <div
        className="h-2 bg-blue-600 transition-[width] duration-300 ease-out"
        style={{ width: `${pct}%` }}
      />
    </div>
  );
}
