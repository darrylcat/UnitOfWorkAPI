# Unit Of Work API
## Introduction
This is a proof of concept project designed to explore how to run selection SQL queries on SQL Server when another process is in the middle of transaction and is blocking access to the database.

It is not designed to be production ready, and no warranty is provided for it's use.

## UnitOfWorkService
The core concept is the UnitOfWorkService which is not an actual Unit of Work service, but designed as a wrapper for EF Core's UoW implementation (if someone has a better name for it please feel free to keep it to yourself!).

The UnitOfWorkService is designed to be a singleton service, shared by all domain services in the application.

At it's heart is the property **DBContext _sharedContext**, which is used as the application's sole connection to the physical database.

It presents asynchronous functions **SelectAsync, InsertAsync, UpdateAsync and DeleteAsync** to provide CRUD operations.

It also presents **GetDatabaseLockAsync** and **ReleaseDataLockAsync** to manage exclusive access to the database.

**Warning!!!** Select operations conducted when a database is locked, may return data that is uncommitted, and may be reverted by the locking process.

If you need committed data, then wrap your select operations with **GetDatabaseLockAsync** and **ReleaseDataLockAsync** (see **UserDetailService.GetUser**).

The locking process is designed to achive several things:
1. Ensure that all currently executing queries complete first.
2. Ensure that any request for database lock is served on a first come first served basis.
3. Ensure that if a process which has the current lock cancels, the transactions are reverted and the lock released.
4. Pause new selection queries when a process is releasing a lock.

## Testing
The application has been tested by adding a 50 delay after an update operation in UserDetailService.Update, and a lock process in UserDetailService.GetUser.

First the UserDetail controller is called with an update request UserDetailController.Update, followed by the Get User request Get("id") and a Get Paged Query Get([FromQuery] UserDetailPagedQuery).

The get paged query should return first, then after a delay, the update user and then the get user. The get user query should show the new user details.
