import { PackageNode, PatchNode, SessionNode, ConflictNode, InstanceNode } from "../types";
import { PackageDemoData, PatchDemoData, SessionDemoData, ConflictDemoData, InstanceDemoData } from "./demo";

export class DemoDataProvider {
  static getPatchesData(): PatchNode[] {
    return PatchDemoData.getPatchesData();
  }

  static getSessionsData(): SessionNode[] {
    return SessionDemoData.getSessionsData();
  }

  static getConflictsData(): ConflictNode[] {
    return ConflictDemoData.getConflictsData();
  }

  static getEnhancedPackagesData(): PackageNode[] {
    return PackageDemoData.getEnhancedPackagesData();
  }

  static getInstancesData(): InstanceNode[] {
    return InstanceDemoData.getInstancesData();
  }
}

// Re-export types for backward compatibility
export { PackageNode, PatchNode, SessionNode, ConflictNode, InstanceNode };