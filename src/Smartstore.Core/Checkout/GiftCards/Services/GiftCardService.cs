﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Smartstore.Core.Common;
using Smartstore.Core.Data;
using Smartstore.Core.Identity;

namespace Smartstore.Core.Checkout.GiftCards
{
    public partial class GiftCardService : IGiftCardService
    {
        private readonly SmartDbContext _db;
        private readonly IWorkContext _workContext;

        public GiftCardService(SmartDbContext db, IWorkContext workContext)
        {
            _db = db;
            _workContext = workContext;
        }

        public virtual Task<List<AppliedGiftCard>> GetValidGiftCardsAsync(int storeId = 0, Customer customer = null)
        {
            var currency = _workContext.WorkingCurrency;
            var query = _db.GiftCards
                .Include(x => x.PurchasedWithOrderItem)
                    .ThenInclude(x => x.Order)
                .Include(x => x.GiftCardUsageHistory)
                .ApplyStandardFilter(storeId)                
                .AsQueryable();

            if (customer != null)
            {
                // Get gift card codes applied by customer
                var couponCodes = customer.GenericAttributes.GiftCardCouponCodes;
                if (!couponCodes.Any())
                    return Task.FromResult(new List<AppliedGiftCard>());

                query = query.ApplyCouponFilter(couponCodes.Select(x => x.Value).ToArray());
            }

            // Get valid gift cards (remaining useable amount > 0)
            var giftCards = query
                .Select(x => new AppliedGiftCard
                {
                    GiftCard = x,
                    UsableAmount = new(x.Amount - x.GiftCardUsageHistory.Where(y => y.GiftCardId == x.Id).Sum(x => x.UsedValue), currency)
                })
                .Where(x => x.UsableAmount > decimal.Zero)
                .ToListAsync();

            return giftCards;
        }

        //TODO: (ms) (core) have order item & order eager loaded
        public virtual bool ValidateGiftCard(GiftCard giftCard, int storeId = 0)
        {
            Guard.NotNull(giftCard, nameof(giftCard));

            if (!giftCard.IsGiftCardActivated)
                return false;

            var orderStoreId = giftCard.PurchasedWithOrderItem?.Order?.StoreId ?? null;
            return (storeId == 0 || orderStoreId is null || orderStoreId == storeId) && GetRemainingAmount(giftCard) > decimal.Zero;
        }

        //TODO: (ms) (core) have giftcard usage history eager loaded
        public virtual Money GetRemainingAmount(GiftCard giftCard)
        {
            Guard.NotNull(giftCard, nameof(giftCard));

            var usedValue = giftCard.GiftCardUsageHistory?.Sum(x => x.UsedValue) ?? decimal.Zero;

            return _workContext.WorkingCurrency.AsMoney(giftCard.Amount - usedValue, false, true);
        }

        public virtual Task<string> GenerateGiftCardCodeAsync()
        {
            var length = 13;
            var result = Guid.NewGuid().ToString();
            if (result.Length > length)
            {
                result = result.Substring(0, length);
            }

            return Task.FromResult(result);
        }
    }
}