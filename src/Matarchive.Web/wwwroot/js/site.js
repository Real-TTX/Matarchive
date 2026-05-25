document.addEventListener("submit", (event) => {
  const form = event.target;
  const confirmMessage = form && form.dataset ? form.dataset.confirm : null;
  if (!confirmMessage) {
    return;
  }

  if (!window.confirm(confirmMessage)) {
    event.preventDefault();
  }
});

document.addEventListener("click", async (event) => {
  if (!(event.target instanceof Element)) {
    return;
  }

  const target = event.target.closest("[data-copy-text]");
  if (!target) {
    return;
  }

  const text = target.getAttribute("data-copy-text");
  if (!text) {
    return;
  }

  try {
    await navigator.clipboard.writeText(text);
    const label = target.querySelector("[data-copy-label]");
    if (label) {
      const original = label.textContent;
      label.textContent = "Kopiert";
      window.setTimeout(() => {
        label.textContent = original;
      }, 1600);
    }
  } catch {
    window.prompt("Kopiere den Wert manuell:", text);
  }
});

(() => {
  const connectionTypeConfig = {
    EMAIL: {
      defaultPort: 993,
      hostLabel: "Posteingangsserver",
      heading: "Profil fuer E-Mail",
      description: "Ein Mailprofil kann per IMAP/POP3 lesen und per SMTP schreiben. Die Richtung wird pro Profil aktiviert.",
      remotePathLabel: "Mailbox / Ordner",
      remotePathPlaceholder: "z. B. INBOX oder Archiv",
      showPort: true,
      showRemotePath: true,
      showSsl: true,
      commonEndpoint: false,
      defaultRead: true,
      defaultWrite: false,
    },
    SMB: {
      defaultPort: 445,
      hostLabel: "Server",
      heading: "Profil fuer SMB",
      description: "Dateifreigabe als Quelle oder Ziel. Host und Freigabe/Pfad bleiben getrennt.",
      remotePathLabel: "Freigabe / Pfad",
      remotePathPlaceholder: "z. B. Backup\\Test1",
      showPort: false,
      showRemotePath: true,
      showSsl: false,
      commonEndpoint: true,
      defaultRead: true,
      defaultWrite: true,
    },
    CUSTOM: {
      defaultPort: 0,
      hostLabel: "Zielhost",
      heading: "Profil fuer Benutzerdefiniert",
      description: "Freies Dateisystemprofil fuer lokale oder gemountete Pfade im Container.",
      remotePathLabel: "Pfad / Kontext",
      remotePathPlaceholder: "Freier Zielpfad",
      showPort: true,
      showRemotePath: true,
      showSsl: true,
      commonEndpoint: true,
      defaultRead: true,
      defaultWrite: true,
    },
  };

  const getTypeConfig = (type) => {
    const key = (type || "").trim().toUpperCase();
    if (key === "POP3" || key === "IMAP" || key === "MAIL" || key === "E-MAIL") {
      return connectionTypeConfig.EMAIL;
    }

    return connectionTypeConfig[key] || connectionTypeConfig.EMAIL;
  };

  const getIncomingPort = (protocol) => {
    return (protocol || "").toUpperCase() === "POP3" ? "110" : "993";
  };

  const getCapabilitySummary = (canRead, canWrite) => {
    if (canRead && canWrite) {
      return "Lesen + Schreiben";
    }

    if (canRead) {
      return "nur Lesen";
    }

    if (canWrite) {
      return "nur Schreiben";
    }

    return "ohne Richtung";
  };

  const setHidden = (element, hidden) => {
    if (!element) {
      return;
    }

    element.classList.toggle("is-hidden", hidden);
  };

  const setSectionHidden = (section, hidden) => {
    setHidden(section, hidden);
    if (!section) {
      return;
    }

    section.querySelectorAll("input, select, textarea").forEach((control) => {
      control.disabled = hidden;
    });
  };

  const updateText = (elements, value) => {
    elements.forEach((element) => {
      element.textContent = value;
    });
  };

  const initConnectionForm = (form) => {
    const typeSelect = form.querySelector("[data-connection-type-select]");
    if (!(typeSelect instanceof HTMLSelectElement)) {
      return;
    }

    const commonSection = form.querySelector("[data-connection-section='common']");
    const emailReadSection = form.querySelector("[data-connection-section='emailRead']");
    const emailWriteSection = form.querySelector("[data-connection-section='emailWrite']");
    const portFields = form.querySelectorAll("[data-connection-field='port']");
    const sslFields = form.querySelectorAll("[data-connection-field='ssl']");
    const remotePathFields = form.querySelectorAll("[data-connection-field='remotePath']");
    const hostLabels = form.querySelectorAll("[data-connection-label='host']");
    const remotePathLabels = form.querySelectorAll("[data-connection-label='remotePath']");
    const headingTargets = form.querySelectorAll("[data-connection-heading]");
    const descriptionTargets = form.querySelectorAll("[data-connection-description]");
    const capabilityTargets = form.querySelectorAll("[data-connection-capability-summary]");
    const remoteInputs = form.querySelectorAll("[data-connection-remote-input]");
    const portInputs = form.querySelectorAll("[data-connection-port-input]");
    const smtpPortInput = form.querySelector("[data-connection-smtp-port-input]");
    const incomingProtocol = form.querySelector("[data-connection-incoming-protocol]");
    const canReadInput = form.querySelector("[data-connection-capability-read]");
    const canWriteInput = form.querySelector("[data-connection-capability-write]");

    const isChecked = (input) => input instanceof HTMLInputElement && input.checked;

    const applyType = (updateDefaults) => {
      const config = getTypeConfig(typeSelect.value);
      const isEmail = !config.commonEndpoint;

      if (updateDefaults) {
        if (canReadInput instanceof HTMLInputElement) {
          canReadInput.checked = config.defaultRead;
        }

        if (canWriteInput instanceof HTMLInputElement) {
          canWriteInput.checked = config.defaultWrite;
        }
      }

      const canRead = isChecked(canReadInput);
      const canWrite = isChecked(canWriteInput);

      setSectionHidden(commonSection, !config.commonEndpoint);
      setSectionHidden(emailReadSection, !isEmail || !canRead);
      setSectionHidden(emailWriteSection, !isEmail || !canWrite);

      portFields.forEach((field) => setHidden(field, !config.showPort));
      sslFields.forEach((field) => setHidden(field, !config.showSsl));
      remotePathFields.forEach((field) => setHidden(field, !config.showRemotePath));

      updateText(hostLabels, config.hostLabel);
      updateText(remotePathLabels, config.remotePathLabel);
      updateText(headingTargets, config.heading);
      updateText(descriptionTargets, config.description);
      updateText(capabilityTargets, getCapabilitySummary(canRead, canWrite));

      remoteInputs.forEach((remoteInput) => {
        if (remoteInput instanceof HTMLInputElement) {
          remoteInput.placeholder = config.remotePathPlaceholder;
        }
      });

      if (updateDefaults) {
        portInputs.forEach((portInput) => {
          if (portInput instanceof HTMLInputElement) {
            portInput.value = isEmail ? getIncomingPort(incomingProtocol?.value) : config.defaultPort > 0 ? String(config.defaultPort) : "";
          }
        });

        if (smtpPortInput instanceof HTMLInputElement && !smtpPortInput.value) {
          smtpPortInput.value = "587";
        }
      }
    };

    typeSelect.addEventListener("change", () => applyType(true));

    if (canReadInput instanceof HTMLInputElement) {
      canReadInput.addEventListener("change", () => applyType(false));
    }

    if (canWriteInput instanceof HTMLInputElement) {
      canWriteInput.addEventListener("change", () => applyType(false));
    }

    if (incomingProtocol instanceof HTMLSelectElement) {
      incomingProtocol.addEventListener("change", () => {
        portInputs.forEach((portInput) => {
          if (portInput instanceof HTMLInputElement) {
            portInput.value = getIncomingPort(incomingProtocol.value);
          }
        });
      });
    }

    applyType(false);
  };

  const init = () => {
    document.querySelectorAll("[data-connection-form]").forEach((form) => initConnectionForm(form));
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
