namespace UnitOfWorkAPI.Models.Database;

public class UserDetail : BaseRecord
{
    public string UserName { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public bool Active { get; set; }
}
