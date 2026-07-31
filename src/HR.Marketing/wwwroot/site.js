const header = document.querySelector(".site-header");
const menuToggle = document.querySelector(".menu-toggle");
const navLinks = document.querySelectorAll(".site-nav a, .header-actions a");

menuToggle?.addEventListener("click", () => {
  const isOpen = header.classList.toggle("nav-open");
  menuToggle.setAttribute("aria-expanded", String(isOpen));
});

navLinks.forEach((link) => {
  link.addEventListener("click", () => {
    header.classList.remove("nav-open");
    menuToggle?.setAttribute("aria-expanded", "false");
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

  monthly = Math.max(monthly, minimumMonthlyCharge);
  const effectivePricePerEmployee = employees > 0 ? monthly / employees : 0;

  return { monthly, effectivePricePerEmployee };
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
  const effectivePriceElement = pricingEstimator.querySelector("[data-effective-price]");
  const ctaStandard = pricingEstimator.querySelector("[data-cta-standard]");
  const ctaLarge = pricingEstimator.querySelector("[data-cta-large]");
  const ctaMessage = pricingEstimator.querySelector("[data-cta-message]");
  const largeOrganisationThreshold = Number.parseInt(pricingEstimator.dataset.largeOrgThreshold, 10) || 500;

  let lastValidEmployees = Number.parseInt(employeeCountInput?.value, 10) || 0;

  const standardMessage = "Perfect for self-service setup. You can start your free trial today.";
  const largeOrganisationMessage =
    "Larger organisations often benefit from a guided implementation. We'd be happy to help you plan your rollout.";

  function render(employees) {
    const { monthly, effectivePricePerEmployee } = calculatePricing(employees);
    const isLargeOrganisation = employees > largeOrganisationThreshold;

    if (activeEmployeesElement) activeEmployeesElement.textContent = String(employees);
    if (monthlyPriceElement) monthlyPriceElement.textContent = `${currencyFormatter.format(monthly)}/month`;
    if (effectivePriceElement) {
      effectivePriceElement.textContent = `Equivalent to ${currencyFormatter.format(effectivePricePerEmployee)} per employee`;
    }

    if (ctaStandard) ctaStandard.hidden = isLargeOrganisation;
    if (ctaLarge) ctaLarge.hidden = !isLargeOrganisation;
    if (ctaMessage) ctaMessage.textContent = isLargeOrganisation ? largeOrganisationMessage : standardMessage;

    return { monthly, isLargeOrganisation };
  }

  function handleInput(source, rawValue) {
    const { value, validationMessage } = parseEmployeeCount(rawValue);

    if (errorElement) {
      errorElement.hidden = !validationMessage;
      errorElement.textContent = validationMessage ?? "";
    }
    employeeCountInput?.setAttribute("aria-invalid", String(Boolean(validationMessage)));

    if (validationMessage) {
      return;
    }

    lastValidEmployees = value;

    if (source !== employeeCountInput && employeeCountInput) {
      employeeCountInput.value = String(value);
    }
    if (source !== employeeSlider && employeeSlider) {
      employeeSlider.value = String(Math.min(value, Number(employeeSlider.max)));
    }

    const { monthly, isLargeOrganisation } = render(value);

    trackEvent("Employee Count Changed", {
      activeEmployees: value,
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

const contactForm = document.querySelector("[data-contact-form]");
const formStatus = document.querySelector("[data-form-status]");

function markFieldValidity(field) {
  const wrapper = field.closest(".form-field");
  if (!wrapper) {
    return true;
  }

  const isValid = field.checkValidity();
  wrapper.classList.toggle("is-invalid", !isValid);
  return isValid;
}

function validateContactForm(form) {
  const fields = [...form.querySelectorAll("input, textarea")];
  const invalidFields = fields.filter((field) => !markFieldValidity(field));
  const invalidField = invalidFields[0];
  return { isValid: !invalidField, invalidField };
}

async function submitContactPlaceholder(form) {
  const formData = new FormData(form);
  const payload = Object.fromEntries(formData.entries());

  // TODO: Replace with the real email/CRM service when one exists.
  return {
    ok: true,
    reference: `OBT-${Date.now()}`,
    payload,
  };
}

contactForm?.addEventListener("input", (event) => {
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
    markFieldValidity(event.target);
  }
});

contactForm?.addEventListener("submit", async (event) => {
  event.preventDefault();

  const { isValid, invalidField } = validateContactForm(contactForm);
  if (!isValid) {
    invalidField?.focus();
    if (formStatus) {
      formStatus.hidden = false;
      formStatus.textContent = "Please complete the required fields before sending.";
    }
    return;
  }

  const result = await submitContactPlaceholder(contactForm);
  if (formStatus && result.ok) {
    formStatus.hidden = false;
    formStatus.textContent = "Thanks. Your details have been captured in this contact form. A real email service can be connected next.";
  }
});
