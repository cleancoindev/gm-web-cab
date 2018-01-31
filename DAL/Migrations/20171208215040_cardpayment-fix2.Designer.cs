﻿// <auto-generated />
using Goldmint.Common;
using Goldmint.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using System;

namespace Goldmint.DAL.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20171208215040_cardpayment-fix2")]
    partial class cardpaymentfix2
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn)
                .HasAnnotation("ProductVersion", "2.0.1-rtm-125");

            modelBuilder.Entity("Goldmint.DAL.Models.Card", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("CardHolder")
                        .HasColumnName("card_holder")
                        .HasMaxLength(128);

                    b.Property<string>("CardMasked")
                        .HasColumnName("card_masked")
                        .HasMaxLength(64);

                    b.Property<string>("GWCustomerId")
                        .IsRequired()
                        .HasColumnName("gw_customer_id")
                        .HasMaxLength(32);

                    b.Property<string>("GWInitTransactionId")
                        .IsRequired()
                        .HasColumnName("gw_init_transaction_id")
                        .HasMaxLength(64);

                    b.Property<int>("State")
                        .HasColumnName("state");

                    b.Property<DateTime?>("TimeCompleted")
                        .HasColumnName("time_completed");

                    b.Property<DateTime>("TimeCreated")
                        .HasColumnName("time_created");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("gm_card");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.CardPayment", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<double>("Amount")
                        .HasColumnName("amount");

                    b.Property<long>("CardId")
                        .HasColumnName("card_id");

                    b.Property<int>("Currency")
                        .HasColumnName("currency");

                    b.Property<string>("DeskTicketId")
                        .IsRequired()
                        .HasColumnName("desk_ticket_id")
                        .HasMaxLength(32);

                    b.Property<string>("GWTransactionId")
                        .HasColumnName("gw_transaction_id")
                        .HasMaxLength(64);

                    b.Property<string>("ProviderMessage")
                        .HasColumnName("provider_message")
                        .HasMaxLength(512);

                    b.Property<string>("ProviderStatus")
                        .HasColumnName("provider_status")
                        .HasMaxLength(64);

                    b.Property<int?>("RefEntity")
                        .HasColumnName("ref_entity");

                    b.Property<long?>("RefEntityId")
                        .HasColumnName("ref_entity_id");

                    b.Property<int>("Status")
                        .HasColumnName("status");

                    b.Property<DateTime?>("TimeCompleted")
                        .HasColumnName("time_completed");

                    b.Property<DateTime>("TimeCreated")
                        .HasColumnName("time_created");

                    b.Property<DateTime>("TimeNextCheck")
                        .HasColumnName("time_next_check");

                    b.Property<string>("TransactionId")
                        .IsRequired()
                        .HasColumnName("transaction_id")
                        .HasMaxLength(32);

                    b.Property<int>("Type")
                        .HasColumnName("type");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("CardId");

                    b.HasIndex("UserId");

                    b.ToTable("gm_card_payment");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Deposit", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<double>("Amount")
                        .HasColumnName("amount");

                    b.Property<int>("Currency")
                        .HasColumnName("currency");

                    b.Property<string>("DeskTicketId")
                        .IsRequired()
                        .HasColumnName("desk_ticket_id")
                        .HasMaxLength(32);

                    b.Property<string>("EthTransactionId")
                        .HasColumnName("eth_transaction_id")
                        .HasMaxLength(66);

                    b.Property<int>("Source")
                        .HasColumnName("source");

                    b.Property<long>("SourceId")
                        .HasColumnName("source_id");

                    b.Property<int>("Status")
                        .HasColumnName("status");

                    b.Property<DateTime?>("TimeCompleted")
                        .HasColumnName("time_completed");

                    b.Property<DateTime>("TimeCreated")
                        .HasColumnName("time_created");

                    b.Property<DateTime>("TimeNextCheck")
                        .HasColumnName("time_next_check");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("gm_deposit");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.Role", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnName("concurrency_stamp");

                    b.Property<string>("Name")
                        .HasColumnName("name")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedName")
                        .HasColumnName("normalized_name")
                        .HasMaxLength(256);

                    b.HasKey("Id");

                    b.HasIndex("NormalizedName")
                        .IsUnique()
                        .HasName("RoleNameIndex");

                    b.ToTable("gm_role");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.RoleClaim", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("ClaimType")
                        .HasColumnName("claim_type");

                    b.Property<string>("ClaimValue")
                        .HasColumnName("claim_value");

                    b.Property<long>("RoleId")
                        .HasColumnName("role_id");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("gm_role_claim");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.User", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<int>("AccessFailedCount")
                        .HasColumnName("access_failed_count");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnName("concurrency_stamp");

                    b.Property<string>("Email")
                        .HasColumnName("email")
                        .HasMaxLength(256);

                    b.Property<bool>("EmailConfirmed")
                        .HasColumnName("email_confirmed");

                    b.Property<bool>("LockoutEnabled")
                        .HasColumnName("lockout_enabled");

                    b.Property<DateTimeOffset?>("LockoutEnd")
                        .HasColumnName("lockout_end");

                    b.Property<string>("NormalizedEmail")
                        .HasColumnName("normalized_email")
                        .HasMaxLength(256);

                    b.Property<string>("NormalizedUserName")
                        .HasColumnName("normalized_username")
                        .HasMaxLength(256);

                    b.Property<string>("PasswordHash")
                        .HasColumnName("password_hash");

                    b.Property<string>("PhoneNumber")
                        .HasColumnName("phone_number");

                    b.Property<bool>("PhoneNumberConfirmed")
                        .HasColumnName("phone_number_confirmed");

                    b.Property<string>("SecurityStamp")
                        .HasColumnName("security_stamp");

                    b.Property<string>("TFASecret")
                        .IsRequired()
                        .HasColumnName("tfa_secret")
                        .HasMaxLength(32);

                    b.Property<DateTime>("TimeRegistered")
                        .HasColumnName("time_registered");

                    b.Property<bool>("TwoFactorEnabled")
                        .HasColumnName("tfa_enabled");

                    b.Property<string>("UserName")
                        .HasColumnName("username")
                        .HasMaxLength(256);

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasName("UserNameIndex");

                    b.ToTable("gm_user");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserClaim", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("ClaimType")
                        .HasColumnName("claim_type");

                    b.Property<string>("ClaimValue")
                        .HasColumnName("claim_value");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("gm_user_claim");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserLogin", b =>
                {
                    b.Property<string>("LoginProvider")
                        .HasColumnName("login_provider");

                    b.Property<string>("ProviderKey")
                        .HasColumnName("provider_key");

                    b.Property<string>("ProviderDisplayName")
                        .HasColumnName("provider_display_name");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("gm_user_login");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserRole", b =>
                {
                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.Property<long>("RoleId")
                        .HasColumnName("role_id");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("gm_user_role");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserToken", b =>
                {
                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.Property<string>("LoginProvider")
                        .HasColumnName("login_provider");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<string>("Value")
                        .HasColumnName("value");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("gm_user_token");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.KycShuftiProTicket", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("CallbackMessage")
                        .HasColumnName("callback_message")
                        .HasMaxLength(128);

                    b.Property<string>("CallbackStatusCode")
                        .HasColumnName("callback_status_code")
                        .HasMaxLength(32);

                    b.Property<string>("CountryCode")
                        .IsRequired()
                        .HasColumnName("country_code")
                        .HasMaxLength(2);

                    b.Property<DateTime>("DoB")
                        .HasColumnName("dob");

                    b.Property<string>("FirstName")
                        .IsRequired()
                        .HasColumnName("first_name")
                        .HasMaxLength(64);

                    b.Property<bool>("IsVerified")
                        .HasColumnName("is_verified");

                    b.Property<string>("LastName")
                        .IsRequired()
                        .HasColumnName("last_name")
                        .HasMaxLength(64);

                    b.Property<string>("Method")
                        .IsRequired()
                        .HasColumnName("method")
                        .HasMaxLength(32);

                    b.Property<string>("PhoneNumber")
                        .IsRequired()
                        .HasColumnName("phone_number")
                        .HasMaxLength(32);

                    b.Property<string>("ReferenceId")
                        .IsRequired()
                        .HasColumnName("reference_id")
                        .HasMaxLength(32);

                    b.Property<DateTime>("TimeCreated")
                        .HasColumnName("time_created");

                    b.Property<DateTime>("TimeResponded")
                        .HasColumnName("time_responed");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("gm_kyc_shuftipro_ticket");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Mutex", b =>
                {
                    b.Property<string>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasMaxLength(64);

                    b.Property<DateTime>("Expires")
                        .IsConcurrencyToken()
                        .HasColumnName("expires");

                    b.Property<string>("Locker")
                        .IsConcurrencyToken()
                        .IsRequired()
                        .HasColumnName("locker")
                        .HasMaxLength(32);

                    b.HasKey("Id");

                    b.ToTable("gm_mutex");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.UserOptions", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<bool>("InitialTFAQuest")
                        .HasColumnName("initial_tfa_quest");

                    b.Property<bool>("PrimaryAgreementRead")
                        .HasColumnName("primary_agreement_read");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId")
                        .IsUnique();

                    b.ToTable("gm_user_options");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.UserVerification", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<string>("Apartment")
                        .HasColumnName("apartment")
                        .HasMaxLength(128);

                    b.Property<string>("City")
                        .HasColumnName("city")
                        .HasMaxLength(256);

                    b.Property<string>("Country")
                        .HasColumnName("country")
                        .HasMaxLength(64);

                    b.Property<string>("CountryCode")
                        .HasColumnName("country_code")
                        .HasMaxLength(2);

                    b.Property<DateTime?>("DoB")
                        .HasColumnName("dob");

                    b.Property<string>("FirstName")
                        .HasColumnName("first_name")
                        .HasMaxLength(64);

                    b.Property<long?>("KycShuftiProTicketId")
                        .HasColumnName("kyc_shuftipro_ticket_id");

                    b.Property<string>("LastName")
                        .HasColumnName("last_name")
                        .HasMaxLength(64);

                    b.Property<string>("MiddleName")
                        .HasColumnName("middle_name")
                        .HasMaxLength(64);

                    b.Property<string>("PhoneNumber")
                        .HasColumnName("phone_number")
                        .HasMaxLength(32);

                    b.Property<string>("PostalCode")
                        .HasColumnName("postal_code")
                        .HasMaxLength(16);

                    b.Property<string>("State")
                        .HasColumnName("state")
                        .HasMaxLength(256);

                    b.Property<string>("Street")
                        .HasColumnName("street")
                        .HasMaxLength(256);

                    b.Property<DateTime?>("TimeUserChanged")
                        .HasColumnName("time_user_changed");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("KycShuftiProTicketId");

                    b.HasIndex("UserId")
                        .IsUnique();

                    b.ToTable("gm_user_verification");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Withdraw", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id")
                        .HasAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

                    b.Property<double>("Amount")
                        .HasColumnName("amount");

                    b.Property<int>("Currency")
                        .HasColumnName("currency");

                    b.Property<string>("DeskTicketId")
                        .IsRequired()
                        .HasColumnName("desk_ticket_id")
                        .HasMaxLength(32);

                    b.Property<int>("Destination")
                        .HasColumnName("destination");

                    b.Property<long>("DestinationId")
                        .HasColumnName("destination_id");

                    b.Property<string>("EthTransactionId")
                        .HasColumnName("eth_transaction_id")
                        .HasMaxLength(66);

                    b.Property<int>("Status")
                        .HasColumnName("status");

                    b.Property<DateTime?>("TimeCompleted")
                        .HasColumnName("time_completed");

                    b.Property<DateTime>("TimeCreated")
                        .HasColumnName("time_created");

                    b.Property<DateTime>("TimeNextCheck")
                        .HasColumnName("time_next_check");

                    b.Property<long>("UserId")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("gm_withdraw");
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Card", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithMany("Card")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.CardPayment", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Card", "Card")
                        .WithMany()
                        .HasForeignKey("CardId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithMany("CardPayment")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Deposit", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithMany("Deposit")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.RoleClaim", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.Role")
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserClaim", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserLogin", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserRole", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.Role")
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("Goldmint.DAL.Models.Identity.User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Identity.UserToken", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.KycShuftiProTicket", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithMany("KycShuftiProTicket")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.UserOptions", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithOne("UserOptions")
                        .HasForeignKey("Goldmint.DAL.Models.UserOptions", "UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.UserVerification", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.KycShuftiProTicket", "KycShuftiProTicket")
                        .WithMany()
                        .HasForeignKey("KycShuftiProTicketId");

                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithOne("UserVerification")
                        .HasForeignKey("Goldmint.DAL.Models.UserVerification", "UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("Goldmint.DAL.Models.Withdraw", b =>
                {
                    b.HasOne("Goldmint.DAL.Models.Identity.User", "User")
                        .WithMany("Withdraw")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade);
                });
#pragma warning restore 612, 618
        }
    }
}
