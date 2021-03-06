﻿using Goldmint.Common;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Goldmint.DAL.Models {

	[Table("gm_user_finhistory")]
	public class UserFinHistory : BaseUserEntity, IConcurrentUpdate {

		[Column("type"), Required]
		public UserFinHistoryType Type { get; set; }

		[Column("status"), Required]
		public UserFinHistoryStatus Status { get; set; }

		[Column("source"), MaxLength(128), Required]
		public string Source { get; set; }

		[Column("source_amount"), MaxLength(FieldMaxLength.BlockchainCurrencyAmount)]
		public string SourceAmount { get; set; }

		[Column("destination"), MaxLength(128)]
		public string Destination { get; set; }

		[Column("destination_amount"), MaxLength(FieldMaxLength.BlockchainCurrencyAmount)]
		public string DestinationAmount { get; set; }

		[Column("comment"), MaxLength(FieldMaxLength.Comment), Required]
		public string Comment { get; set; }

		[Column("time_created"), Required]
		public DateTime TimeCreated { get; set; }

		[Column("concurrency_stamp"), MaxLength(FieldMaxLength.ConcurrencyStamp), ConcurrencyCheck]
		public string ConcurrencyStamp { get; set; }

		// ---

		public void OnConcurrencyStampRegen() {
			this.ConcurrencyStamp = ConcurrentStamp.GetGuid();
		}

	}
}
