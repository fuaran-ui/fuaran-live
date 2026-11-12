export interface ArmTally {
  arm: string;
  passed: number;
  failed: number;
  skipped: number;
  notRun: string[];
  ran: boolean;
}
export function armOf(file: string): string;
export function tallyArms(report: unknown): ArmTally[];
export function formatArms(arms: ArmTally[]): string[];
export function exitCodeFor(vitestStatus: number, arms: ArmTally[], strict: boolean): number;
