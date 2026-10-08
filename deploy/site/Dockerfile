# One Dark site as an image: a published `dark`, the site's source and config, and `start-site`, which
# brings the store on the volume up to date and serves the router. `build-image` assembles the context.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble

RUN useradd --create-home --shell /bin/sh dark && mkdir -p /data && chown dark:dark /data

COPY --chown=dark:dark dark /home/dark/bin/dark
COPY --chown=dark:dark start-site /home/dark/bin/start-site
COPY --chown=dark:dark site /home/dark/site

# The store, the policy and every row the site writes live here. Mount a volume on it: without one,
# all of that goes with the container, and with one, the volume is the only copy (rows never sync).
ENV DARK_CONFIG_RUNDIR=/data
VOLUME /data

USER dark
ENV PORT=8080
EXPOSE 8080
CMD ["/home/dark/bin/start-site"]
