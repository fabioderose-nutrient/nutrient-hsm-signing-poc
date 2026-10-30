FROM debian:bookworm-slim
RUN apt-get update \
 && apt-get install -y --no-install-recommends softhsm2 openssl \
 && rm -rf /var/lib/apt/lists/*
COPY --chmod=0755 init.sh /usr/local/bin/init.sh
ENTRYPOINT ["/usr/local/bin/init.sh"]
