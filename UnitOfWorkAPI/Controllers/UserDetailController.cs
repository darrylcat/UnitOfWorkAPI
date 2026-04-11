using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using UnitOfWorkAPI.Models.DTOs.Data;
using UnitOfWorkAPI.Models.DTOs.Requests;
using UnitOfWorkAPI.Models.DTOs.Responses;
using UnitOfWorkAPI.Services;

namespace UnitOfWorkAPI.Controllers;

/// <summary>
/// Provides access to the User Detail records
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class UserDetailController : ControllerBase
{
    private readonly ILogger<UserDetailController> logger;
    private readonly IUserDetailService userDetailService;

    public UserDetailController(ILogger<UserDetailController> logger, IUserDetailService userDetailService)
    {
        this.logger = logger;
        this.userDetailService = userDetailService;
    }

    /// <summary>
    /// Allows client to search the user details table, and return paginated results
    /// </summary>
    /// <param name="userDetailPagedQuery">Contains the parameters required for searching the entities</param>
    [HttpGet()]
    [ProducesResponseType<UserDetailPagedQueryResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] UserDetailPagedQuery userDetailPagedQuery, CancellationToken cancellationToken)
    {
        try
        {
            var result = await userDetailService.GetPagedQuery(userDetailPagedQuery, cancellationToken);
            if (result == null || result.ErrorMessages.Any())
            {
                return BadRequest(result == null ? "Unable to retrieve User Details" : String.Join("", result.ErrorMessages));
            }
            return Ok(result);
        }
        catch (TaskCanceledException tce)
        {
            logger.LogError(tce.Message);
            throw;
        }
    }

    /// <summary>
    /// Returns a single user details record based on id
    /// </summary>
    /// <param name="id"></param>
    [HttpGet("{id}")]
    [ProducesResponseType<UserDetailDTO>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromRoute] int id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await userDetailService.GetUser(id, cancellationToken));
        }
        catch (Exception ex) { 
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Creates a new user details record 
    /// </summary>
    /// <param name="userDetail"></param>
    /// <returns>The new user detail record</returns>
    [HttpPost()]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ActionResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(BadRequestObjectResult))]
    public async Task<IActionResult> Create([FromBody] UserDetailDTO userDetail, CancellationToken cancellationToken)
    {
        try
        {
            return CreatedAtAction("Create", await userDetailService.Create(userDetail, cancellationToken));
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

    }

    /// <summary>
    /// Update an existing record
    /// </summary>
    /// <param name="id">Unique identifier of the record to update</param>
    /// <param name="dto">New details for the record</param>
    /// <returns>NoContent result</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent, Type = typeof(NoContent))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(NotFoundResult))]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UserDetailDTO dto, CancellationToken cancellationToken)
    {
        try
        {
            if(await userDetailService.Update(id, dto, cancellationToken))
            {
                return NoContent();
            }
        }
        catch (Exception ex) {
            logger.LogError(ex.Message);
        }
        return NotFound();
    }
}
