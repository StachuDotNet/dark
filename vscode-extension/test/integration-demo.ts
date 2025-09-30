/**
 * Integration Demo Test Suite
 *
 * This script demonstrates and validates the complete integration
 * of our VS Code extension, testing real user workflows from
 * tree navigation to content rendering.
 */

import * as assert from 'assert';
import * as vscode from 'vscode';
import { ComprehensiveDarkContentProvider } from '../client/src/providers/comprehensiveDarkContentProvider';
import { UrlPatternRouter } from '../client/src/providers/urlPatternRouter';
import { ScenarioManager, DevelopmentScenario } from '../client/src/data/scenarioManager';
import { SessionsTreeDataProvider } from '../client/src/providers/treeviews/sessionsTreeDataProvider';
import { InstancesTreeDataProvider } from '../client/src/providers/treeviews/instancesTreeDataProvider';
import { EnhancedPackagesTreeDataProvider } from '../client/src/providers/treeviews/enhancedPackagesTreeDataProvider';

/**
 * Integration Test Scenarios
 *
 * Each test simulates a complete user workflow from tree interaction
 * to content viewing, validating that all integration points work correctly.
 */
export class IntegrationDemoTests {
  private contentProvider: ComprehensiveDarkContentProvider;
  private scenarioManager: ScenarioManager;
  private sessionsProvider: SessionsTreeDataProvider;
  private instancesProvider: InstancesTreeDataProvider;
  private packagesProvider: EnhancedPackagesTreeDataProvider;

  constructor() {
    this.contentProvider = new ComprehensiveDarkContentProvider();
    this.scenarioManager = ScenarioManager.getInstance();
    this.sessionsProvider = new SessionsTreeDataProvider();
    this.instancesProvider = new InstancesTreeDataProvider();
    this.packagesProvider = new EnhancedPackagesTreeDataProvider();
  }

  /**
   * Scenario 1: Active Development Workflow
   *
   * Simulates a developer working on authentication features,
   * navigating from session overview to specific patch operations.
   */
  async testActiveDevelopmentWorkflow(): Promise<void> {
    console.log('🧪 Testing Active Development Workflow...');

    // 1. Set scenario to active development
    this.scenarioManager.setScenario(DevelopmentScenario.ActiveDevelopment);

    // 2. Verify sessions tree shows active development data
    const sessionData = await this.sessionsProvider.getChildren();
    assert(sessionData.length > 0, 'Sessions tree should have data');

    const sessionManagement = sessionData.find(node =>
      node.contextValue === 'session-management'
    );
    assert(sessionManagement, 'Should have session management category');

    // 3. Simulate clicking on current session info
    const sessionChildren = await this.sessionsProvider.getChildren(sessionManagement);
    const currentSession = sessionChildren?.find(node =>
      node.contextValue === 'current-session-info'
    );
    assert(currentSession, 'Should have current session info');

    // 4. Verify the click command would open the correct URL
    const sessionTreeItem = this.sessionsProvider.getTreeItem(currentSession);
    assert(sessionTreeItem.command?.command === 'darklang.session.view',
      'Current session should have view command');

    // 5. Test content provider handles session URL correctly
    const sessionUri = vscode.Uri.parse('dark:///session/feature-auth');
    const sessionContent = this.contentProvider.provideTextDocumentContent(sessionUri);

    assert(sessionContent.includes('feature-auth'), 'Session content should include session name');
    assert(sessionContent.includes('Authentication'), 'Session content should include context');
    assert(sessionContent.includes('stachu'), 'Session content should include owner');
    assert(sessionContent.includes('Active Patches'), 'Session content should show patch info');

    // 6. Simulate navigating to patch operations
    const operationsUri = vscode.Uri.parse('dark:///patch/current/operations');
    const operationsContent = this.contentProvider.provideTextDocumentContent(operationsUri);

    assert(operationsContent.includes('Operation Summary'), 'Operations content should have summary');
    assert(operationsContent.includes('MyApp.User.validate'), 'Should show specific operations');
    assert(operationsContent.includes('Performance Impact'), 'Should include performance analysis');

    console.log('✅ Active Development Workflow test passed');
  }

  /**
   * Scenario 2: Instance Exploration Workflow
   *
   * Simulates exploring remote instances and browsing packages,
   * testing the instance tree to content navigation flow.
   */
  async testInstanceExplorationWorkflow(): Promise<void> {
    console.log('🧪 Testing Instance Exploration Workflow...');

    // 1. Get instances tree data
    const instanceData = await this.instancesProvider.getChildren();
    assert(instanceData.length > 0, 'Instances tree should have data');

    // 2. Find remote instance category
    const remoteCategory = instanceData.find(node =>
      node.contextValue === 'remote-category'
    );
    assert(remoteCategory, 'Should have remote instances category');

    // 3. Get remote instances
    const remoteInstances = await this.instancesProvider.getChildren(remoteCategory);
    const matterInstance = remoteInstances?.find(node =>
      node.id === 'matter-instance'
    );
    assert(matterInstance, 'Should have matter.darklang.com instance');

    // 4. Find instance packages
    const instanceChildren = await this.instancesProvider.getChildren(matterInstance);
    const packagesNode = instanceChildren?.find(node =>
      node.contextValue === 'remote-packages'
    );
    assert(packagesNode, 'Should have packages node for instance');

    // 5. Test clicking on packages opens correct URL
    const packagesTreeItem = this.instancesProvider.getTreeItem(packagesNode);
    assert(packagesTreeItem.command?.command === 'darklang.instance.browse.packages',
      'Packages node should have browse command');

    // 6. Test instance packages content
    const packagesUri = vscode.Uri.parse('dark:///instance/packages?instance=matter-instance');
    const packagesContent = this.contentProvider.provideTextDocumentContent(packagesUri);

    assert(packagesContent.includes('matter.darklang.com'), 'Should show instance name');
    assert(packagesContent.includes('Darklang.Stdlib'), 'Should show standard library');
    assert(packagesContent.includes('Package Overview'), 'Should have package overview');
    assert(packagesContent.includes('156'), 'Should show realistic package count');

    // 7. Test namespace navigation
    const namespaceUri = vscode.Uri.parse('dark:///instance/namespace?name=Darklang.Stdlib.List&instance=matter-instance');
    const namespaceContent = this.contentProvider.provideTextDocumentContent(namespaceUri);

    assert(namespaceContent.includes('Darklang.Stdlib.List'), 'Should show namespace name');
    assert(namespaceContent.includes('Core Library'), 'Should identify as core library');
    assert(namespaceContent.includes('map'), 'Should list map function');
    assert(namespaceContent.includes('filter'), 'Should list filter function');

    console.log('✅ Instance Exploration Workflow test passed');
  }

  /**
   * Scenario 3: Package Development Workflow
   *
   * Simulates browsing packages and viewing different content types,
   * testing the package tree to content navigation flow.
   */
  async testPackageDevelopmentWorkflow(): Promise<void> {
    console.log('🧪 Testing Package Development Workflow...');

    // 1. Get packages tree data
    const packageData = await this.packagesProvider.getChildren();
    assert(packageData.length > 0, 'Packages tree should have data');

    // 2. Find a specific function in the tree (this tests the scenario system)
    // Note: The actual structure depends on the current scenario
    const stdlibNodes = packageData.filter(node =>
      node.label.includes('Darklang.Stdlib') || node.label.includes('Stdlib')
    );
    assert(stdlibNodes.length > 0, 'Should have standard library packages');

    // 3. Test package content with different views
    const packageUri = vscode.Uri.parse('dark:///package/Darklang.Stdlib.List.map');
    const sourceContent = this.contentProvider.provideTextDocumentContent(packageUri);

    assert(sourceContent.includes('map'), 'Package content should include function name');
    assert(sourceContent.includes('List'), 'Package content should include type information');

    // 4. Test documentation view
    const docsUri = vscode.Uri.parse('dark:///package/Darklang.Stdlib.List.map?view=docs');
    const docsContent = this.contentProvider.provideTextDocumentContent(docsUri);

    assert(docsContent.includes('Documentation'), 'Docs view should have documentation header');
    assert(docsContent.includes('Examples'), 'Docs should include examples');
    assert(docsContent.includes('Performance'), 'Docs should include performance info');

    // 5. Test types view
    const typesUri = vscode.Uri.parse('dark:///package/Darklang.Stdlib.List.map?view=types');
    const typesContent = this.contentProvider.provideTextDocumentContent(typesUri);

    assert(typesContent.includes('Type Information'), 'Types view should have type header');
    assert(typesContent.includes('Function Signature'), 'Should show function signature');
    assert(typesContent.includes("'a -> 'b"), 'Should show polymorphic types');

    // 6. Test namespace overview
    const namespaceOverviewUri = vscode.Uri.parse('dark:///package/namespace?path=Darklang.Stdlib.List');
    const namespaceOverviewContent = this.contentProvider.provideTextDocumentContent(namespaceOverviewUri);

    assert(namespaceOverviewContent.includes('Namespace Overview'), 'Should have namespace header');
    assert(namespaceOverviewContent.includes('Standard Library'), 'Should identify as stdlib');
    assert(namespaceOverviewContent.includes('list manipulation'), 'Should describe purpose');

    console.log('✅ Package Development Workflow test passed');
  }

  /**
   * Scenario 4: URL Pattern Integration Test
   *
   * Tests the URL routing system comprehensively to ensure
   * all supported patterns parse and generate content correctly.
   */
  async testUrlPatternIntegration(): Promise<void> {
    console.log('🧪 Testing URL Pattern Integration...');

    const testUrls = [
      // Session URLs
      'dark:///session/feature-auth',
      'dark:///session/feature-auth/export',
      'dark:///session/team-session-alpha/transfer',

      // Patch URLs
      'dark:///patch/abc123',
      'dark:///patch/abc123/operations',
      'dark:///patch/abc123/conflicts',
      'dark:///patch/abc123/tests',
      'dark:///patch/abc123/test?name=validate_empty_email_returns_error',

      // Instance URLs
      'dark:///instance/local',
      'dark:///instance/remote',
      'dark:///instance/packages?instance=matter-instance',
      'dark:///instance/sessions?instance=matter-instance',
      'dark:///instance/namespace?name=Darklang.Stdlib.List&instance=matter-instance',
      'dark:///instance/registry',

      // Package URLs
      'dark:///package/Darklang.Stdlib.List.map',
      'dark:///package/Darklang.Stdlib.List.map?view=docs',
      'dark:///package/Darklang.Stdlib.List.map?view=types',
      'dark:///package/Darklang.Stdlib.List.map?view=ast',
      'dark:///package/namespace?path=Darklang.Stdlib.List',

      // Config URLs
      'dark:///config',
      'dark:///config/user',
      'dark:///config/sync'
    ];

    for (const url of testUrls) {
      console.log(`  Testing URL: ${url}`);

      // 1. Test URL parsing
      const parsedUrl = UrlPatternRouter.parseUrl(url);
      assert(parsedUrl !== null, `URL should parse correctly: ${url}`);
      assert(parsedUrl.scheme === 'dark', `Scheme should be 'dark': ${url}`);

      // 2. Test content generation
      const uri = vscode.Uri.parse(url);
      const content = this.contentProvider.provideTextDocumentContent(uri);

      assert(content.length > 100, `Content should be substantial for: ${url}`);
      assert(!content.includes('Error'), `Content should not contain errors for: ${url}`);
      assert(content.includes('#'), `Content should be markdown formatted: ${url}`);

      // 3. Test content relevance
      if (url.includes('session')) {
        assert(content.toLowerCase().includes('session'), `Session URL should contain session content: ${url}`);
      }
      if (url.includes('patch')) {
        assert(content.toLowerCase().includes('patch'), `Patch URL should contain patch content: ${url}`);
      }
      if (url.includes('package')) {
        assert(content.toLowerCase().includes('package') || content.toLowerCase().includes('function'),
          `Package URL should contain package content: ${url}`);
      }
    }

    console.log('✅ URL Pattern Integration test passed');
  }

  /**
   * Scenario 5: Multi-Scenario State Test
   *
   * Tests that the scenario system properly updates content
   * across different development contexts.
   */
  async testMultiScenarioState(): Promise<void> {
    console.log('🧪 Testing Multi-Scenario State...');

    const scenarios = [
      DevelopmentScenario.CleanStart,
      DevelopmentScenario.ActiveDevelopment,
      DevelopmentScenario.ReadyForReview,
      DevelopmentScenario.ConflictResolution,
      DevelopmentScenario.TeamCollaboration
    ];

    for (const scenario of scenarios) {
      console.log(`  Testing scenario: ${scenario}`);

      // 1. Set scenario
      this.scenarioManager.setScenario(scenario);

      // 2. Test that session data changes
      const sessionData = await this.sessionsProvider.getChildren();
      assert(sessionData.length > 0, `Session data should exist for scenario: ${scenario}`);

      // 3. Test that content reflects scenario
      const sessionUri = vscode.Uri.parse('dark:///session/current');
      const content = this.contentProvider.provideTextDocumentContent(sessionUri);

      // Different scenarios should have different content characteristics
      if (scenario === DevelopmentScenario.CleanStart) {
        assert(content.includes('clean') || content.includes('new') || content.includes('start'),
          'Clean start should reference new/clean content');
      } else if (scenario === DevelopmentScenario.ConflictResolution) {
        assert(content.includes('conflict') || content.includes('merge'),
          'Conflict resolution should reference conflicts');
      } else if (scenario === DevelopmentScenario.TeamCollaboration) {
        assert(content.includes('team') || content.includes('collaboration') || content.includes('shared'),
          'Team collaboration should reference team content');
      }
    }

    console.log('✅ Multi-Scenario State test passed');
  }

  /**
   * Run All Integration Tests
   *
   * Executes the complete test suite and reports results.
   */
  async runAllTests(): Promise<void> {
    console.log('🚀 Starting Integration Demo Tests...\n');

    const startTime = Date.now();
    let passed = 0;
    let failed = 0;

    const tests = [
      () => this.testActiveDevelopmentWorkflow(),
      () => this.testInstanceExplorationWorkflow(),
      () => this.testPackageDevelopmentWorkflow(),
      () => this.testUrlPatternIntegration(),
      () => this.testMultiScenarioState()
    ];

    for (const test of tests) {
      try {
        await test();
        passed++;
      } catch (error) {
        console.error(`❌ Test failed: ${error}`);
        failed++;
      }
    }

    const duration = Date.now() - startTime;

    console.log(`\n📊 Test Results:`);
    console.log(`  Passed: ${passed}`);
    console.log(`  Failed: ${failed}`);
    console.log(`  Duration: ${duration}ms`);

    if (failed === 0) {
      console.log(`\n🎉 All integration tests passed! The VS Code extension is fully integrated and working correctly.`);
    } else {
      console.log(`\n⚠️ Some tests failed. Please review the integration.`);
    }
  }
}

/**
 * Demo Test Runner
 *
 * Instantiate and run the integration tests to validate
 * the complete system integration.
 */
export async function runIntegrationDemo(): Promise<void> {
  const demo = new IntegrationDemoTests();
  await demo.runAllTests();
}

// Export for VS Code test runner
export { IntegrationDemoTests };