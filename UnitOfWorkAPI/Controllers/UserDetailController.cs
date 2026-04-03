using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UnitOfWorkAPI.Models.DTOs.Requests;
using UnitOfWorkAPI.Services;

namespace UnitOfWorkAPI.Controllers;

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

    [HttpGet()]
    public async Task<IActionResult> Get([FromQuery] UserDetailPagedQuery userDetailPagedQuery, CancellationToken cancellationToken)
    {
        var result = await userDetailService.GetPagedQuery(userDetailPagedQuery, cancellationToken);
        if (result == null || result.ErrorMessages.Any()) {
            return BadRequest(result == null ? "Unable to retrieve User Details" : String.Join("" , result.ErrorMessages));
        } 
        return Ok(result);
    }

    [HttpGet("{id}")]
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
}
