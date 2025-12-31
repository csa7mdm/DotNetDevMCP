# ADR-001: Fork SharpTools Instead of Pluggable Integration

**Status**: Accepted
**Date**: 2025-12-30
**Deciders**: Ahmed Mustafa

## Context

We need to build DotNetDevMCP with comprehensive .NET development capabilities. SharpTools already provides excellent Roslyn-based code intelligence. We evaluated three options:

1. **Pluggable Integration**: Keep SharpTools separate, call it via MCP protocol
2. **Hybrid**: Reference SharpTools.Tools as a library, build our server around it
3. **Fork & Merge**: Fork SharpTools, integrate all code, add our features

### Trade-offs

**Pluggable (Option 1)**:
- ✅ Separation of concerns
- ✅ Can update SharpTools independently
- ✅ Lighter codebase
- ❌ Inter-MCP communication overhead
- ❌ Two servers to configure/manage
- ❌ Harder to optimize across features

**Hybrid (Option 2)**:
- ✅ Tight integration
- ✅ Single process
- ✅ Can contribute improvements upstream
- ❌ Dependency management complexity
- ❌ Limited by library API boundaries

**Fork & Merge (Option 3)**:
- ✅ Complete control over codebase
- ✅ Single unified tool
- ✅ Can optimize across all features
- ✅ Simpler deployment (one MCP server)
- ❌ Maintenance burden
- ❌ Diverges from upstream
- ✅ Can still contribute improvements back

## Decision

**We will fork SharpTools and merge it into DotNetDevMCP** (Option 3).

### Rationale

1. **User's Vision**: "One tool to rule them all" - a single comprehensive MCP server
2. **Optimization Potential**: Can optimize across code intelligence + orchestration
3. **Simpler UX**: Users configure one MCP server, not multiple
4. **Complete Control**: No limitations imposed by library boundaries
5. **MIT License**: Legally permissive, allows forking with attribution

### Implementation Plan

1. Clone SharpTools repository
2. Integrate into `DotNetDevMCP.CodeIntelligence` namespace
3. Maintain attribution in LICENSE and documentation
4. Refactor to align with our architecture (service layer pattern)
5. Extend with concurrent operations support

## Consequences

### Positive

- **Unified Tool**: Single MCP server for all .NET development needs
- **Deep Integration**: Can share Roslyn workspaces across features
- **Performance**: Eliminate MCP protocol overhead for internal calls
- **Customization**: Full control to optimize for our use cases
- **Simplified Deployment**: One server to install and configure

### Negative

- **Maintenance Responsibility**: We own all the code, including SharpTools portions
- **Upstream Divergence**: May drift from original SharpTools over time
- **Attribution Required**: Must properly credit SharpTools in all materials
- **Larger Codebase**: More code to understand and maintain

### Mitigation Strategies

- **Maintain Attribution**: LICENSE file clearly credits кɵɵѕнī
- **Document Origin**: Mark SharpTools-derived code with comments
- **Consider Contributions**: If we make improvements to core intelligence, consider contributing back
- **Modular Design**: Keep code intelligence in separate projects for clarity

## References

- [SharpTools GitHub Repository](https://github.com/kooshi/SharpToolsMCP)
- [MIT License](https://opensource.org/licenses/MIT)
- User requirements discussion (2025-12-30)

## Notes

SharpTools creator welcomes contributions and stated "I intend to maintain and improve it for as long as I am using it." While we're forking, we can still contribute improvements back if beneficial to both projects.
