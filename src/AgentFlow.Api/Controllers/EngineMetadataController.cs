using Microsoft.AspNetCore.Mvc;
using AgentFlow.Abstractions;

namespace AgentFlow.Api.Controllers;

[ApiController]
[Route("api/engine")]
public class EngineMetadataController : ControllerBase
{
    [HttpGet("loop-templates")]
    public IActionResult GetLoopTemplates()
    {
        return Ok(new
        {
            standardReact = new
            {
                name = "ReAct (Standard)",
                description = "Default autonomous cognitive loop: Think -> Act -> Observe.",
                mode = RuntimeMode.Autonomous,
                steps = new[]
                {
                    new { id = "step-think", type = "think", label = "Meta-Cognition (Think)", description = "LLM analyzes intent and picks the next tool." },
                    new { id = "step-act", type = "act", label = "Action (Act)", description = "Execution of the selected tool in a secure sandbox." },
                    new { id = "step-observe", type = "observe", label = "Perception (Observe)", description = "LLM interprets tool output and evaluates goal progress." }
                }
            },
            deterministicFlow = new
            {
                name = "Fixed Sequence (Deterministic)",
                description = "Strict execution of a pre-defined sequence of tools.",
                mode = RuntimeMode.Deterministic,
                steps = new[]
                {
                    new { id = "step-plan", type = "plan", label = "Execution Plan", description = "Maps out the static steps to be executed." }
                }
            }
        });
    }
}
