import * as vscode from "vscode";

// Core data structures
export interface PackageNode {
  id: string;
  label: string;
  type: "namespace" | "module" | "function" | "type" | "constant";
  children?: PackageNode[];
  collapsibleState: number; // 0 = collapsed, 1 = expanded
  contextValue?: string;
  packagePath?: string;
}

export interface PatchNode {
  id: string;
  label: string;
  type: "current" | "draft" | "incoming" | "applied" | "sync-status";
  author?: string;
  intent?: string;
  contextValue: string;
  children?: PatchNode[];
  status?: string;
  operations?: number;
  conflicts?: number;
  tests?: number;
}

export interface SessionNode {
  id: string;
  label: string;
  type: "current" | "recent" | "shared" | "actions" | "patch" | "operation" | "conflict";
  contextValue: string;
  children?: SessionNode[];
  patchData?: {
    status?: "draft" | "ready" | "applied" | "conflicts";
    operations?: number;
    conflicts?: number;
    tests?: number;
    intent?: string;
    author?: string;
  };
}

export interface ConflictNode {
  id: string;
  label: string;
  type: "session-summary" | "patch" | "conflict-item" | "resolution";
  status?: "ready" | "conflicts" | "draft";
  contextValue: string;
  children?: ConflictNode[];
}

export interface InstanceNode {
  id: string;
  label: string;
  type: "current" | "remote" | "local" | "category" | "packages" | "sessions" | "patches";
  contextValue: string;
  children?: InstanceNode[];
  instanceData?: {
    url?: string;
    path?: string;
    status?: "connected" | "disconnected" | "syncing";
    packageCount?: number;
    sessionCount?: number;
    patchCount?: number;
  };
}

// URL Pattern types
export type UrlMode = 'package' | 'edit' | 'draft' | 'patch' | 'history' | 'compare' | 'session' | 'config' | 'instance';

export interface ParsedUrl {
  mode: UrlMode;
  target?: string;
  view?: string;
  context?: string;
  params?: { [key: string]: string };
}

// Content Provider interfaces
export interface ContentProvider {
  getContent(parsedUrl: ParsedUrl): string;
}

// Status bar data
export interface StatusBarData {
  session: {
    name: string;
    active: boolean;
  };
  patch: {
    current: string;
    changes: number;
  };
  conflicts: {
    count: number;
    hasUnresolved: boolean;
  };
  sync: {
    incoming: number;
    outgoing: number;
  };
}

// Tree view item states
export type TreeItemState = "normal" | "modified" | "new" | "conflict" | "resolved";

// Command context
export interface CommandContext {
  extensionUri: vscode.Uri;
  statusBarManager: any; // TODO: Type this properly
}

// Demo data interfaces
export interface DemoScenario {
  name: string;
  description: string;
  execute: () => void;
}

export interface ValidationError {
  field: string;
  message: string;
  severity: "error" | "warning";
}

// Patch operations
export interface PatchOperation {
  type: "create" | "modify" | "delete";
  target: string;
  description: string;
  content?: string;
  diff?: string;
}

// User and authentication
export interface User {
  id: string;
  name: string;
  email: string;
  avatar?: string;
}

export interface Session {
  id: string;
  name: string;
  intent: string;
  author: User;
  createdAt: Date;
  isActive: boolean;
}

export interface Patch {
  id: string;
  title: string;
  intent: string;
  author: User;
  operations: PatchOperation[];
  status: "draft" | "ready" | "applied" | "conflicts";
  createdAt: Date;
  updatedAt: Date;
}

// Package information
export interface PackageInfo {
  name: string;
  namespace: string;
  version: string;
  description?: string;
  functions: FunctionInfo[];
  types: TypeInfo[];
  constants: ConstantInfo[];
}

export interface FunctionInfo {
  name: string;
  signature: string;
  description?: string;
  parameters: ParameterInfo[];
  returnType: string;
  examples?: string[];
}

export interface TypeInfo {
  name: string;
  definition: string;
  description?: string;
  variants?: string[];
}

export interface ConstantInfo {
  name: string;
  type: string;
  value: string;
  description?: string;
}

export interface ParameterInfo {
  name: string;
  type: string;
  description?: string;
  optional?: boolean;
}