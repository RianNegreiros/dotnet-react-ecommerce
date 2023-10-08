using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuctionsController : ControllerBase
{
  private readonly AuctionDbContext _context;
  private readonly IMapper _mapper;
  private readonly IPublishEndpoint _publishEndpoint;

  public AuctionsController(AuctionDbContext context, IMapper mapper, IPublishEndpoint publishEndpoint)
  {
    _context = context;
    _mapper = mapper;
    _publishEndpoint = publishEndpoint;
  }

  [HttpGet]
  public async Task<ActionResult<List<AuctionDto>>> GetAuctions(string? date)
  {
    var query = _context.Auctions.OrderBy(x => x.Item.Make).AsQueryable();

    if (!string.IsNullOrEmpty(date))
      query = query.Where(x => x.UpdatedAt.CompareTo(DateTime.Parse(date).ToUniversalTime()) > 0);

    return await query.ProjectTo<AuctionDto>(_mapper.ConfigurationProvider).ToListAsync();
  }

  [HttpGet("{id}")]
  public async Task<ActionResult<AuctionDto>> GetAuction(Guid id)
  {
    var auction = await _context.Auctions
      .Include(x => x.Item)
      .FirstOrDefaultAsync(x => x.Id == id);

    if (auction == null)
      return NotFound();

    return Ok(_mapper.Map<AuctionDto>(auction));
  }

  [HttpPost]
  public async Task<ActionResult<AuctionDto>> CreateAuction(CreateAuctionDto createAuctionDto)
  {
    var auction = _mapper.Map<Auction>(createAuctionDto);

    _context.Auctions.Add(auction);
    auction.Seller = "test";

    var result = await _context.SaveChangesAsync() > 0;

    var newAction = _mapper.Map<AuctionDto>(auction);

    await _publishEndpoint.Publish(_mapper.Map<AuctionCreated>(newAction));

    if (!result)
      return BadRequest();

    return CreatedAtAction(nameof(GetAuction), new { id = auction.Id }, _mapper.Map<AuctionDto>(auction));
  }

  [HttpPut("{id}")]
  public async Task<IActionResult> UpdateAuction(Guid id, UpdateAuctionDto updateAuctionDto)
  {
    var auction = await _context.Auctions
      .Include(x => x.Item)
      .FirstOrDefaultAsync(x => x.Id == id);

    if (auction == null)
      return NotFound();

    auction.Item.Make = updateAuctionDto.Make ?? auction.Item.Make;
    auction.Item.Model = updateAuctionDto.Model ?? auction.Item.Model;
    auction.Item.Year = updateAuctionDto.Year ?? auction.Item.Year;
    auction.Item.Mileage = updateAuctionDto.Mileage ?? auction.Item.Mileage;
    auction.Item.Color = updateAuctionDto.Color ?? auction.Item.Color;

    await _publishEndpoint.Publish(_mapper.Map<AuctionUpdated>(auction));

    var result = await _context.SaveChangesAsync() > 0;

    if (!result)
      return BadRequest();

    return Ok();
  }

  [HttpDelete("{id}")]
  public async Task<IActionResult> DeleteAuction(Guid id)
  {
    var auction = await _context.Auctions.FindAsync(id);

    if (auction == null)
      return NotFound();

    _context.Auctions.Remove(auction);

    await _publishEndpoint.Publish<AuctionDeleted>(new { Id = auction.Id.ToString() });

    var result = await _context.SaveChangesAsync() > 0;

    if (!result)
      return BadRequest();

    return Ok();
  }
}
