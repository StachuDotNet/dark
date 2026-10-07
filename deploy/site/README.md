# Deploying a Dark site

A site is a directory: `site.conf` and `src/<Owner.Module>.dark`. `build-image` turns it into a Docker image, and the image serves it from a store on a volume. `example/` is one: two sites behind one router, and a DB.

---

## What one deploy is

One image, one app, one volume, serving every site the router answers for. The router dispatches on the `Host` header in plain Dark (`example/src/Example.Sites.dark`).

So several small sites share:
- one machine, one bill,
- one store, and one volume to back up.

The cost: one bad deploy takes every site on it down together, and they restart together.

At every start the image runs `start-site` against the store on the volume. It authors the source, creates the missing DBs, commits, approves the router, and serves it pinned, so a change reaches the site only as a new image and a restart. Its comments say why each step is there; the one to know is that an approval belongs to one version of the router, so a start without it would serve the new code and refuse every route that touches a DB.

## The data

- **Rows live in the store on the volume, and nowhere else.** They are not in git, not in sync and not in the image.
- **A restart keeps them, and so does a redeploy**, because it mounts the same volume.
- **Losing the volume loses them.** Nothing in this directory backs them up. Fly's volume snapshots are the only copy beyond the volume itself.

## Before it ships: the check

`build-image` starts every image it builds once against an empty volume, so a site that will not commit fails there. The router has to answer `GET /ping`, which `fly.toml` health-checks too.

---

## What only you can do, in order

Everything up to step 4 is local and was run here. Everything after it touches your Fly account and your DNS, and none of it has been run or verified.

1. **Make the site directory.** Copy `example/` and edit it:
   - `site.conf`: set `ROUTER`, list your DBs in `DBS` as `Name=Owner.Module.Type`, and add `ALLOW` rules if the code reads env vars or calls out over HTTP.
   - `src/`: one file per module, named `Owner.Module.dark`, holding that module's declarations.
   - Your sites' code is in your own store. `dark view <name> --raw` prints one item exactly as `dark module` takes it back; do that per item into the file.
   - The router answers `/ping`, then dispatches on host. `Example.Sites.router` is the shape.
2. **Build it, from a clone of current main with its container up:**

       deploy/site/build-image path/to/your-site --tag stachu-sites:1

   - It publishes `dark` (AOT; a couple of minutes) and builds the image.
   - It ends with `checked: it starts from an empty volume and serves`.
   - If not, it prints why and stops.
3. **Try it locally:**

       docker run -d --name sites -p 8080:8080 -v sites-data:/data stachu-sites:1
       curl -H 'Host: stachu.net' localhost:8080/

4. **Restart it and look again** (`docker restart sites`). What you wrote should still be there. Then `docker rm -f sites`.
5. **Create the app and its volume**, once. The app name never goes in a file:

       fly apps create <app>
       fly volumes create site_data --size 1 --region iad -a <app>

6. **Deploy the image:**

       fly deploy --config deploy/site/fly.toml -a <app> --image stachu-sites:1 --local-only

   Unverified: that `--image` with `--local-only` takes a locally built image and pushes it. If it does not, `fly deploy --config deploy/site/fly.toml -a <app>` from a directory holding the build context (`rundir/site-image/<name>`) builds it remotely instead.
7. **Check it answers:** `curl https://<app>.fly.dev/ping`, then a page with your host header.
8. **Check the volume's snapshots before anything you care about lives there:** `fly volumes list -a <app>`, then `fly volumes snapshots list <volume-id>`. The relay's are daily and kept five days. That is the only backup this setup has.
9. **For each domain:**
   - `fly certs add <domain> -a <app>`;
   - add the records it prints at the registrar (Squarespace for stachu.net, Name.com for darklang.com);
   - `fly certs show <domain> -a <app>` until it says issued.
10. **Every later deploy** is steps 2 and 6 with a new tag. Keep the previous tag.

If a start fails on the host:
- `fly logs -a <app>` shows `start-site`'s own lines.
- `fly deploy ... --image <previous tag>` puts the last good version back.
- The volume, and the rows on it, are untouched either way.
- What Fly does on its own when a new machine's start fails (whether it retries, rolls back, or leaves the app down) has not been checked. The step-2 check exists so that case should not arise from bad code.
