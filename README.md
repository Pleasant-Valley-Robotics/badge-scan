# Team website

Your team's own website. It is part of Pleasant Valley FIRST Robotics and runs on
the program's robotics server. You don't need to have made a website before: every
page is one file, and the words on it are written right there in the file.

| | Address | Who can see it |
|---|---|---|
| **Preview** | `https://dev-<team number>.firstpvrobotics.com` | Mentors (password) |
| **Live site** | `https://<team number>.firstpvrobotics.com` | Everyone |

**Please don't post students' full names or anything a parent wouldn't want
public.** First names and team photos are fine when your team agrees.

## A few words first

- **HTML** is how a web page is written: words wrapped in *tags* such as
  `<h1>A big heading</h1>` or `<p>A paragraph.</p>`. The tag says what the words are;
  the browser decides how they look, using the style sheet.
- **A `.cshtml` file** is an HTML page that can also hold small pieces of C# (a
  programming language). The site turns it into plain HTML each time someone visits.
- **`@`** marks the start of C#. `@DateTime.Now.Year`, for example, is replaced by this
  year's number. If you need a real `@` in your words (an Instagram handle), write `@@`.
  Email addresses like `team@example.com` are fine as they are.
- **`<!-- ... -->`** is a note in the HTML. Visitors don't see it. The notes in these
  files tell you what to change.
- **The style sheet** (`wwwroot/css/site.css`) decides colors, fonts and spacing. You
  don't need to touch it to change words.

## Change the words on the home page

The home page is **`src/TeamSite.Web/Pages/Index.cshtml`**. Open it and change the
words between the tags: your team name, FTC or FRC, your number, your story, the
cards. Each part has a `<!-- note -->` above it saying what goes there. To add a
paragraph, copy a `<p>...</p>` line; to add a card, copy one `<div class="card item">` line.

Three things are in the frame around every page instead,
**`src/TeamSite.Web/Pages/Shared/_Layout.cshtml`**: the team name and logo at the top,
the menu, and the footer. Change "Team Name", "FTC" and "#0" there too.

Pictures go in `src/TeamSite.Web/wwwroot/images/`. A file at
`wwwroot/images/robot.jpg` is shown with `<img src="/images/robot.jpg" alt="Our robot" />`.
The `alt` words describe the picture for people who can't see it.

## Add a page: "Our Robot"

A page is **one file in `Pages/`** and **one link in the menu**. The file's name is
its address: `Pages/Robot.cshtml` is `/robot` (capitals don't matter).

1. Create `src/TeamSite.Web/Pages/Robot.cshtml` with this in it:

   ```html
   @page
   @{
       ViewData["Title"] = "Our Robot";
   }
   <section class="section">
       <div class="container">
           <h1>Our Robot</h1>
           <p>This season's robot picks up game pieces with a two-wheel intake.</p>
       </div>
   </section>
   ```

   The first line, `@page`, tells the site this file is a page. The `Title` is the
   words in the browser tab. Everything after that is your HTML.

2. In `src/TeamSite.Web/Pages/Shared/_Layout.cshtml`, find the menu (`<nav>`) and add
   one line under the Home line:

   ```html
   <li><a href="/robot">Our Robot</a></li>
   ```

3. Run `dotnet test`. One test reads the menu and checks every link in it opens a
   page, so a typo in the address shows up here before anyone sees it.

That's all. Copy any `<section>` from `Index.cshtml` if you want the same cards or
layout on your new page.

## Run it on your computer

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the tools that build
the site) and [Visual Studio Code](https://code.visualstudio.com) (an editor) with the
C# Dev Kit extension. Open this folder in Visual Studio Code, then in its terminal
(**Terminal → New Terminal**):

```bash
dotnet watch --project src/TeamSite.Web     # opens http://localhost:5300 and reloads when you save
dotnet test                                  # runs the tests
```

Press `Ctrl+C` in the terminal to stop the site.

## How a change gets to the website

1. **Make a branch** (your own copy of the code to work on) and edit the files.
2. **Run `dotnet test`.** Fix anything red before you push.
3. **Open a pull request** (a request to add your change) into `main`. GitHub builds
   the site, runs the tests and starts the container. A red ✗ means something is
   broken: click **Details** to see why. You can't merge until it's green.
4. **Merge the pull request.** Within about a minute the robotics server pulls `main`
   and updates the **preview**. Look at it with a mentor.
5. **Publish it live** by creating a release tag (a mentor or team lead does this):
   - On GitHub: **Releases → Draft a new release → Choose a tag**, type the next
     version (for example `v2.0.1`), click **Create new tag**, then **Publish release**.
   - Or in a terminal: `git tag v2.0.1 && git push origin v2.0.1`

   Within about a minute the live site updates. Tags must start with `v`; the newest
   one is what's live.

**If a release is broken**, the server sees that `/healthz` doesn't answer and keeps
the previous version running. To see what happened, open
`https://dev-<team number>.firstpvrobotics.com/_platform/` (mentor password).

## What's in the folder

| File or folder | What it does |
|---|---|
| `src/TeamSite.Web/Pages/Index.cshtml` | **The home page.** Start here |
| `Pages/Shared/_Layout.cshtml` | The frame around every page: the menu at the top, the footer |
| `Pages/NotFound.cshtml` | The page shown when an address doesn't exist (404) |
| `Pages/Error.cshtml` | The page shown if something breaks on the live site (500) |
| `Pages/_ViewStart.cshtml`, `Pages/_ViewImports.cshtml` | Settings for every page. Leave them alone |
| `wwwroot/` | Files sent as they are: `css/site.css`, `images/`, `fonts/` |
| `Program.cs` | Where the site starts. You don't need to change it |
| `tests/TeamSite.Web.Tests/` | The tests GitHub runs on every pull request |

## Files the server needs (leave these alone unless you know why)

- `Dockerfile`: compiles the site and runs it.
- `compose.yaml`: how the server runs the container.
- `robotics.yml`: which address goes to the container. `public: true` opens the live site to everyone.
- `.github/workflows/check.yml`: the checks on every pull request.
