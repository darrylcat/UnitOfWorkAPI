namespace UnitOfWorkAPI.Models.DTOs.Requests;

public class BasePagedQuery
{
    public string SearchFor { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
}
