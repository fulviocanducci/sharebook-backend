﻿using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ShareBook.Domain;
using ShareBook.Domain.Common;
using ShareBook.Domain.Enums;
using ShareBook.Domain.Exceptions;
using ShareBook.Helper.Extensions;
using ShareBook.Helper.Image;
using ShareBook.Repository;
using ShareBook.Repository.Infra;
using ShareBook.Service.Generic;
using ShareBook.Service.Upload;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

namespace ShareBook.Service
{
    public class BookService : BaseService<Book>, IBookService
    {
        private readonly IUploadService _uploadService;
        private readonly IBooksEmailService _booksEmailService;

        public BookService(IBookRepository bookRepository,
            IUnitOfWork unitOfWork, IValidator<Book> validator,
            IUploadService uploadService, IBooksEmailService booksEmailService)
            : base(bookRepository, unitOfWork, validator)
        {
            _uploadService = uploadService;
            _booksEmailService = booksEmailService;
        }

        public Result<Book> Approve(Guid bookId)
        {
            var book = _repository.Get(bookId);
            if (book == null)
                throw new ShareBookException(ShareBookException.Error.NotFound);

            book.Approved = true;
            _repository.Update(book);

            return new Result<Book>(book);
        }

        public IList<dynamic> FreightOptions()
        {
            var enumValues = new List<dynamic>();
            foreach (FreightOption freightOption in Enum.GetValues(typeof(FreightOption)))
            {
                enumValues.Add(new
                {
                    Value = freightOption.ToString(),
                    Text = freightOption.Description()
                });
            }
            return enumValues;
        }

        public IList<Book> Top15NewBooks()
        {
            return _repository.Get().Where(x => x.Approved
             && !x.BookUsers.Any(y => y.Status == DonationStatus.Donated)).OrderByDescending(x => x.CreationDate).Take(15)
                 .Select(u => new Book
                 {
                     Id = u.Id,
                     Title = u.Title,
                     Author = u.Author,
                     Approved = u.Approved,
                     FreightOption = u.FreightOption,
                     ImageUrl = _uploadService.GetImageUrl(u.ImageSlug, "Books"),
                     Slug = u.Slug,
                     User = new User()
                     {
                         Id = u.User.Id,
                         Email = u.User.Email,
                         Name = u.User.Name,
                         Linkedin = u.User.Linkedin,
                         PostalCode = u.User.PostalCode
                     },
                     Category = new Category()
                     {
                         Name = u.Category.Name
                     }
                 }).ToList();
        }

        public IList<Book> Random15Books()
        {
            return _repository.Get().Where(x => x.Approved
            && !x.BookUsers.Any(y => y.Status == DonationStatus.Donated)).OrderBy(x => Guid.NewGuid()).Take(15)
                 .Select(u => new Book
                 {
                     Id = u.Id,
                     Title = u.Title,
                     Author = u.Author,
                     FreightOption = u.FreightOption,
                     Approved = u.Approved,
                     ImageUrl = _uploadService.GetImageUrl(u.ImageSlug, "Books"),
                     Slug = u.Slug,
                     User = new User()
                     {
                         Id = u.User.Id,
                         Email = u.User.Email,
                         Name = u.User.Name,
                         Linkedin = u.User.Linkedin,
                         PostalCode = u.User.PostalCode
                     },
                     Category = new Category()
                     {
                         Name = u.Category.Name
                     }
                 }).ToList();
        }

        public IList<Book> GetAll(int page, int items)
            => _repository.Get().Include(b => b.User).Include(b => b.BookUsers)
            .Skip((page - 1) * items)
            .Take(items).ToList();

        public override Book Get(params object[] keyValues)
        {
            var result = _repository.Get(keyValues);

            result.ImageUrl = _uploadService.GetImageUrl(result.ImageSlug, "Books");


            return result;
        }

        public override Result<Book> Insert(Book entity)
        {
            entity.UserId = new Guid(Thread.CurrentPrincipal?.Identity?.Name);

            var result = Validate(entity);
            if (result.Success)
            {
                entity.ImageSlug = ImageHelper.FormatImageName(entity.ImageName, entity.Title);

                entity.Slug = entity.Title.GenerateSlug();

                result.Value = _repository.Insert(entity);

                result.Value.ImageUrl = _uploadService.UploadImage(entity.ImageBytes, entity.ImageSlug, "Books");

                result.Value.ImageBytes = null;

                _booksEmailService.SendEmailNewBookInserted(entity).Wait();
            }
            return result;
        }

        public override Result<Book> Update(Book entity)
        {

            Result<Book> result = Validate(entity, x =>
                x.Title,
                x => x.Author,
                x => x.Approved,
                x => x.FreightOption,
                x => x.Id);

            if (!result.Success) return result;

            entity.Slug = entity.Title.GenerateSlug();
            result.Value = _repository.UpdateAsync(entity).Result;

            return result;
        }

        public PagedList<Book> ByTitle(string title, int page, int itemsPerPage)
            => SearchBooks(x => (x.Approved
                                && !x.BookUsers.Any(y => y.Status == DonationStatus.Donated))
                                && x.Title.Contains(title), page, itemsPerPage);

        public PagedList<Book> ByAuthor(string author, int page, int itemsPerPage)
            => SearchBooks(x => (x.Approved
                                 && !x.BookUsers.Any(y => y.Status == DonationStatus.Donated))
                                 && x.Author.Contains(author), page, itemsPerPage);

        public PagedList<Book> FullSearch(string criteria, int page, int itemsPerPage, bool isAdmin)
        {
            Expression<Func<Book, bool>> filter = x => (x.Author.Contains(criteria)
                                                        || x.Title.Contains(criteria)
                                                        || x.Category.Name.Contains(criteria))
                                                        && x.Approved
                                                        && !x.BookUsers.Any(y => y.Status == DonationStatus.Donated);

            if (!isAdmin) filter = x => x.Author.Contains(criteria)
                                        || x.Title.Contains(criteria)
                                        || x.Category.Name.Contains(criteria);

            return SearchBooks(filter, page, itemsPerPage);
        }

        public Book BySlug(string slug)
        {
            return _repository.Get().Where(x => x.Slug.Contains(slug)
            && x.Approved && !x.BookUsers.Any(y => y.Status == DonationStatus.Donated))
                 .Select(u => new Book
                 {
                     Id = u.Id,
                     Title = u.Title,
                     Author = u.Author,
                     Approved = u.Approved,
                     FreightOption = u.FreightOption,
                     ImageUrl = _uploadService.GetImageUrl(u.ImageSlug, "Books"),
                     Slug = u.Slug,
                     User = new User()
                     {
                         Id = u.User.Id,
                         Email = u.User.Email,
                         Name = u.User.Name,
                         Linkedin = u.User.Linkedin,
                         PostalCode = u.User.PostalCode
                     },
                     Category = new Category()
                     {
                         Name = u.Category.Name
                     }
                 }).FirstOrDefault();
        }

        public bool UserRequestedBook(Guid bookId)
        {
            var userId = new Guid(Thread.CurrentPrincipal?.Identity?.Name);
            return _repository.Any(x =>
                    x.Id == bookId &&
                    x.BookUsers
                    .Any(y =>
                    y.Status == DonationStatus.WaitingAction
                    && y.UserId == userId
              ));
        }

        public override PagedList<Book> Get<TKey>(Expression<Func<Book, bool>> filter, Expression<Func<Book, TKey>> order, int page, int itemsPerPage)
            => base.Get(filter, order, page, itemsPerPage);

        #region Private
        private PagedList<Book> SearchBooks(Expression<Func<Book, bool>> filter, int page, int itemsPerPage)
        {
            var result = _repository.Get()
                .Where(filter)
                .Select(u => new Book
                {
                    Id = u.Id,
                    Title = u.Title,
                    Author = u.Author,
                    Approved = u.Approved,
                    FreightOption = u.FreightOption,
                    ImageUrl = _uploadService.GetImageUrl(u.ImageSlug, "Books"),
                    Slug = u.Slug,
                    User = new User()
                    {
                        Id = u.User.Id,
                        Email = u.User.Email,
                        Name = u.User.Name,
                        Linkedin = u.User.Linkedin,
                        PostalCode = u.User.PostalCode
                    },
                    Category = new Category()
                    {
                        Name = u.Category.Name

                    }
                });

            return FormatPagedList(result, page, itemsPerPage, result.Count());
        }

        private PagedList<Book> FormatPagedList(IQueryable<Book> list, int page, int itemsPerPage, int total)
        {
            var skip = (page - 1) * itemsPerPage;
            return new PagedList<Book>()
            {
                Page = page,
                ItemsPerPage = itemsPerPage,
                TotalItems = total,
                Items = list.Skip(skip).Take(itemsPerPage).ToList()
            };
        }
        #endregion
    }
}
