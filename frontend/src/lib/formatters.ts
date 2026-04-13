import { formatDistanceToNow, format } from "date-fns";

export function formatRelativeDate(date: string | Date): string {
  return formatDistanceToNow(new Date(date), { addSuffix: true });
}

export function formatAbsoluteDate(date: string | Date): string {
  return format(new Date(date), "MMM d, yyyy HH:mm");
}

export function formatShortDate(date: string | Date): string {
  return format(new Date(date), "MMM d, yyyy");
}
