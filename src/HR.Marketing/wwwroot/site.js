const header = document.querySelector(".site-header");
const menuToggle = document.querySelector(".menu-toggle");
const menuToggleLabel = menuToggle?.querySelector("[data-menu-toggle-label]");
const siteNav = document.querySelector(".site-nav");
const navLinks = document.querySelectorAll(".site-nav a, .header-actions a");

function setMenuOpen(isOpen) {
  header?.classList.toggle("nav-open", isOpen);
  menuToggle?.setAttribute("aria-expanded", String(isOpen));
  if (menuToggleLabel) {
    menuToggleLabel.textContent = isOpen ? "Close navigation" : "Open navigation";
  }
}

function closeMenu({ focusToggle } = {}) {
  if (!header?.classList.contains("nav-open")) return;

  setMenuOpen(false);

  if (focusToggle) {
    menuToggle?.focus();
  }
}

menuToggle?.addEventListener("click", () => {
  const isOpen = !header.classList.contains("nav-open");
  setMenuOpen(isOpen);

  if (isOpen) {
    // Basic focus management: move focus into the menu so keyboard users land on the
    // first link immediately, rather than continuing to tab from the toggle button
    // through content that's now visually below the open menu.
    const firstLink = siteNav?.querySelector("a");
    firstLink?.focus();
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && header?.classList.contains("nav-open")) {
    closeMenu({ focusToggle: true });
  }
});

navLinks.forEach((link) => {
  link.addEventListener("click", () => {
    closeMenu();
  });
});

document.querySelectorAll("[data-video-card]").forEach((card) => {
  const facade = card.querySelector(".video-card-facade");

  facade?.addEventListener("click", () => {
    const youTubeId = card.dataset.youtubeId;
    const title = card.dataset.videoTitle || "Product video";

    const iframe = document.createElement("iframe");
    iframe.src = `https://www.youtube-nocookie.com/embed/${youTubeId}?autoplay=1`;
    iframe.title = title;
    iframe.allow = "accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture";
    iframe.allowFullscreen = true;

    card.replaceChildren(iframe);
    iframe.focus();
  });
});

window.dataLayer = window.dataLayer || [];

function trackEvent(event, payload) {
  window.dataLayer.push({ event, ...payload });
}

const PRICING_TIERS = {
  firstTierLimit: 50,
  secondTierLimit: 150,
  firstTierRate: 2.0,
  secondTierRate: 1.75,
  thirdTierRate: 1.5,
  minimumMonthlyCharge: 20.0,
};

const currencyFormatter = new Intl.NumberFormat("en-GB", {
  style: "currency",
  currency: "GBP",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

function calculatePricing(employees) {
  const { firstTierLimit, secondTierLimit, firstTierRate, secondTierRate, thirdTierRate, minimumMonthlyCharge } = PRICING_TIERS;

  let monthly;
  if (employees <= firstTierLimit) {
    monthly = employees * firstTierRate;
  } else if (employees <= secondTierLimit) {
    monthly = firstTierLimit * firstTierRate + (employees - firstTierLimit) * secondTierRate;
  } else {
    monthly =
      firstTierLimit * firstTierRate +
      (secondTierLimit - firstTierLimit) * secondTierRate +
      (employees - secondTierLimit) * thirdTierRate;
  }

  // Mirrors PricingCalculator.Calculate on the server: capture whether the tier-based total
  // fell below the minimum charge *before* the floor is applied, so callers can announce the
  // minimum-charge state distinctly rather than just showing the floored total.
  const minimumChargeApplied = monthly < minimumMonthlyCharge;
  monthly = Math.max(monthly, minimumMonthlyCharge);
  const effectivePricePerEmployee = employees > 0 ? monthly / employees : 0;

  return { monthly, effectivePricePerEmployee, minimumChargeApplied };
}

function parseEmployeeCount(rawValue) {
  if (rawValue === null || rawValue === undefined || rawValue.trim() === "") {
    return { value: 0, validationMessage: null };
  }

  const trimmed = rawValue.trim();

  if (trimmed.includes(".") || trimmed.includes(",")) {
    return { value: null, validationMessage: "Please enter a whole number of employees, without decimals." };
  }

  if (!/^-?\d+$/.test(trimmed)) {
    return { value: null, validationMessage: "Please enter a valid number of employees." };
  }

  const value = Number.parseInt(trimmed, 10);

  if (value < 0) {
    return { value: null, validationMessage: "Employee count cannot be negative." };
  }

  return { value, validationMessage: null };
}

const pricingEstimator = document.querySelector("[data-pricing-estimator]");

if (pricingEstimator) {
  const employeeCountInput = pricingEstimator.querySelector("#employee-count");
  const employeeSlider = pricingEstimator.querySelector("#employee-slider");
  const errorElement = pricingEstimator.querySelector("#employee-count-error");
  const activeEmployeesElement = pricingEstimator.querySelector("[data-active-employees]");
  const monthlyPriceElement = pricingEstimator.querySelector("[data-monthly-price]");
  const monthlyPriceValueElement = pricingEstimator.querySelector("[data-monthly-price-value]");
  const effectivePriceElement = pricingEstimator.querySelector("[data-effective-price]");
  const ctaStandard = pricingEstimator.querySelector("[data-cta-standard]");
  const ctaLarge = pricingEstimator.querySelector("[data-cta-large]");
  const ctaStandardMessage = pricingEstimator.querySelector("[data-cta-standard-message]");
  const ctaLargeMessage = pricingEstimator.querySelector("[data-cta-large-message]");
  const priceLiveRegion = pricingEstimator.querySelector("[data-price-live-region]");
  // Mirrors PricingCtaResolver.Resolve on the server: employees === threshold is still the
  // "at/below threshold" (self-service) case, only employees > threshold is large-organisation.
  const largeOrganisationThreshold = Number.parseInt(pricingEstimator.dataset.largeOrgThreshold, 10) || 500;

  let lastValidEmployees = Number.parseInt(employeeCountInput?.value, 10) || 0;

  // The visible price updates instantly on every input event so sighted users get immediate
  // feedback while dragging the slider. The aria-live announcement is debounced separately so
  // screen reader users get one coherent "final" announcement after input settles, rather than
  // a new interruption for every pixel of slider movement.
  let liveRegionTimeoutId;

  // Announces the *settled* price to screen readers via the polite role="status" region.
  // Distinct from the always-on-screen result (see monthlyPriceElement/monthlyPriceValueElement),
  // which updates instantly on every input event so sighted users see live feedback while
  // dragging the slider. The three possible states below (custom pricing, minimum charge,
  // standard tiered price) mirror PricingCtaResolver.Resolve and PricingCalculator.Calculate
  // on the server.
  function announcePrice(employees, monthly, effectivePricePerEmployee, isLargeOrganisation, minimumChargeApplied) {
    if (!priceLiveRegion) return;

    window.clearTimeout(liveRegionTimeoutId);
    liveRegionTimeoutId = window.setTimeout(() => {
      if (isLargeOrganisation) {
        priceLiveRegion.textContent =
          `${employees} active employees is a larger organisation — custom pricing applies. ` +
          `Contact sales for a tailored plan.`;
      } else if (minimumChargeApplied) {
        priceLiveRegion.textContent =
          `${employees} active employees: minimum charge of ${currencyFormatter.format(monthly)} ` +
          `per month applies, before VAT.`;
      } else {
        priceLiveRegion.textContent =
          `${employees} active employees: ${currencyFormatter.format(monthly)} per month before VAT, ` +
          `equivalent to ${currencyFormatter.format(effectivePricePerEmployee)} per employee.`;
      }
    }, 400);
  }

  function render(employees) {
    const { monthly, effectivePricePerEmployee, minimumChargeApplied } = calculatePricing(employees);
    const isLargeOrganisation = employees > largeOrganisationThreshold;

    if (activeEmployeesElement) activeEmployeesElement.textContent = String(employees);
    if (monthlyPriceValueElement) {
      monthlyPriceValueElement.textContent = `${currencyFormatter.format(monthly)}/month`;
    } else if (monthlyPriceElement) {
      monthlyPriceElement.textContent = `${currencyFormatter.format(monthly)}/month`;
    }
    if (effectivePriceElement) {
      effectivePriceElement.textContent = `Equivalent to ${currencyFormatter.format(effectivePricePerEmployee)} per employee`;
    }
    announcePrice(employees, monthly, effectivePricePerEmployee, isLargeOrganisation, minimumChargeApplied);

    if (ctaStandard) ctaStandard.hidden = isLargeOrganisation;
    if (ctaStandardMessage) ctaStandardMessage.hidden = isLargeOrganisation;
    if (ctaLarge) ctaLarge.hidden = !isLargeOrganisation;
    if (ctaLargeMessage) ctaLargeMessage.hidden = !isLargeOrganisation;

    return { monthly, isLargeOrganisation };
  }

  function handleInput(source, rawValue) {
    const { value, validationMessage } = parseEmployeeCount(rawValue);

    if (errorElement) {
      errorElement.hidden = !validationMessage;
      errorElement.textContent = validationMessage ?? "";
    }
    employeeCountInput?.setAttribute("aria-invalid", String(Boolean(validationMessage)));
    employeeSlider?.setAttribute("aria-invalid", String(Boolean(validationMessage)));

    if (validationMessage) {
      return;
    }

    // Clamp consistently: the number input allows values above the slider's max (e.g. very
    // large organisations), the slider itself is clamped to its own min/max range. Both inputs
    // share the same underlying `value` used for the price calculation, so results are
    // identical regardless of which control produced the change.
    const clampedValue = Math.max(0, value);
    lastValidEmployees = clampedValue;

    if (source !== employeeCountInput && employeeCountInput) {
      employeeCountInput.value = String(clampedValue);
    }
    if (source !== employeeSlider && employeeSlider) {
      const sliderMax = Number(employeeSlider.max);
      const sliderMin = Number(employeeSlider.min);
      employeeSlider.value = String(Math.min(Math.max(clampedValue, sliderMin), sliderMax));
    }

    const { monthly, isLargeOrganisation } = render(clampedValue);

    trackEvent("Employee Count Changed", {
      activeEmployees: clampedValue,
      estimatedMonthlyCost: Number(monthly.toFixed(2)),
      ctaShown: isLargeOrganisation ? "Contact Sales" : "Start Free Trial",
    });
  }

  employeeCountInput?.addEventListener("input", (event) => handleInput(employeeCountInput, event.target.value));
  employeeSlider?.addEventListener("input", (event) => handleInput(employeeSlider, event.target.value));

  pricingEstimator.addEventListener("submit", (event) => event.preventDefault());

  pricingEstimator.querySelectorAll("[data-track-cta]").forEach((link) => {
    link.addEventListener("click", () => {
      const { monthly } = calculatePricing(lastValidEmployees);
      const eventNameByCta = {
        "start-trial": "Start Free Trial Clicked",
        "contact-sales": "Contact Sales Clicked"
      };
      trackEvent(eventNameByCta[link.dataset.trackCta], {
        activeEmployees: lastValidEmployees,
        estimatedMonthlyCost: Number(monthly.toFixed(2)),
      });
    });
  });

  trackEvent("Pricing Calculator Viewed", {});
}

// The contact form is a conventional HTML <form method="post"> (see Contact.razor / HR.Marketing
// Program.cs's /contact-submit proxy) — this app renders statically with no interactive circuit, so
// real validation and submission happen server-side. This script only adds two client-side niceties:
// disabling the submit button to prevent duplicate submissions while the POST + redirect is in
// flight, and moving keyboard/screen-reader focus to the success/error status banner after a
// server-side round trip.
const contactForm = document.querySelector("[data-contact-form]");
const contactSubmitButton = document.querySelector("[data-contact-submit]");

contactForm?.addEventListener("submit", () => {
  if (contactForm.checkValidity() && contactSubmitButton) {
    contactSubmitButton.disabled = true;
    contactSubmitButton.textContent = "Sending...";
  }
});

document.querySelector("[data-form-status]")?.focus();

// The signup form (SignUp.razor) is a conventional HTML <form method="post"> posted to the
// /signup-submit server-side proxy — no interactive circuit, so authoritative validation happens
// server-side. `novalidate` suppresses the browser's native validation bubble so we can instead
// mark invalid fields accessibly (aria-invalid + an associated, visible error message via
// aria-describedby) and move keyboard focus to the first invalid field ourselves.
const signUpForm = document.querySelector("[data-signup-form]");
const signUpSubmitButton = document.querySelector("[data-signup-submit]");

signUpForm?.addEventListener("submit", (event) => {
  const fields = Array.from(signUpForm.querySelectorAll("input[required]"));
  let firstInvalidField = null;

  fields.forEach((field) => {
    const isValid = field.checkValidity();
    const formField = field.closest(".form-field");

    formField?.classList.toggle("is-invalid", !isValid);
    field.setAttribute("aria-invalid", String(!isValid));

    if (!isValid && !firstInvalidField) {
      firstInvalidField = field;
    }
  });

  if (firstInvalidField) {
    event.preventDefault();
    firstInvalidField.focus();
    return;
  }

  // Prevent duplicate submissions while the signup + real Supabase Auth round trip is in flight —
  // disabling before the browser's own navigation kicks off still lets the (already-valid) POST
  // go through once.
  if (signUpSubmitButton) {
    signUpSubmitButton.disabled = true;
    signUpSubmitButton.textContent = "Creating your account...";
  }
});

document.querySelector("[data-form-status][data-status='error']")?.focus();

// Password show/hide toggle: swaps the input's `type` between "password" and "text" without
// touching its value, so autofill/password managers and pasted values are unaffected. The
// aria-live region below only updates on blur (not on every keystroke) to avoid spamming screen
// readers while the user is still typing.
const passwordField = document.querySelector("[data-password-field]");
const passwordInput = passwordField?.querySelector("#password");
const passwordToggle = passwordField?.querySelector("[data-password-toggle]");
const passwordLive = document.querySelector("#password-live");

passwordToggle?.addEventListener("click", () => {
  const isHidden = passwordInput.type === "password";
  passwordInput.type = isHidden ? "text" : "password";
  passwordToggle.setAttribute("aria-pressed", String(isHidden));
  passwordToggle.textContent = isHidden ? "Hide password" : "Show password";
  passwordInput.focus();
});

passwordInput?.addEventListener("blur", () => {
  if (!passwordLive) return;

  if (passwordInput.value.length > 0 && passwordInput.value.length < 8) {
    passwordLive.textContent = "Password requirement not yet met: at least 8 characters.";
  } else {
    passwordLive.textContent = "";
  }
});

// Terms of Service / Privacy Policy links on the signup form open their document in a <dialog>
// instead of navigating away — losing an in-progress signup to a link click would be a bad
// trade for reading legal text that's also reachable at its own URL (kept as the link's href for
// no-JS visitors and open-in-new-tab). Native <dialog> gives us Escape-to-close, focus trapping,
// and a ::backdrop for free.
document.querySelectorAll("[data-legal-modal-trigger]").forEach((trigger) => {
  const dialog = document.getElementById(trigger.getAttribute("data-legal-modal-trigger"));
  if (!dialog) return;

  trigger.addEventListener("click", (event) => {
    event.preventDefault();
    dialog.showModal();
  });
});

document.querySelectorAll("[data-legal-modal]").forEach((dialog) => {
  dialog.querySelector("[data-legal-modal-close]")?.addEventListener("click", () => dialog.close());

  // Clicking the ::backdrop counts as a click on the <dialog> element itself (its content sits
  // in a child wrapper), so only close when the click target is the dialog, not something inside it.
  dialog.addEventListener("click", (event) => {
    if (event.target === dialog) {
      dialog.close();
    }
  });
});
