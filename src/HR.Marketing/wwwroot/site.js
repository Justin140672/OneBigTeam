const header = document.querySelector(".site-header");
const menuToggle = document.querySelector(".menu-toggle");
const navLinks = document.querySelectorAll(".site-nav a, .header-actions a");
const PRICE_PER_EMPLOYEE = 4;
const currencyFormatter = new Intl.NumberFormat("en-GB", {
  style: "currency",
  currency: "GBP",
  maximumFractionDigits: 0,
});

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

document.querySelectorAll("[data-price-per-employee]").forEach((element) => {
  element.textContent = currencyFormatter.format(PRICE_PER_EMPLOYEE);
});

const employeeCountInput = document.querySelector("#employee-count");
const estimatedPriceElements = document.querySelectorAll("[data-estimated-price]");
const estimatedEmployeeElements = document.querySelectorAll("[data-estimated-employees]");

function updateEstimate() {
  if (!employeeCountInput) {
    return;
  }

  const rawCount = Number.parseInt(employeeCountInput.value, 10);
  const employeeCount = Number.isFinite(rawCount) && rawCount > 0 ? rawCount : 0;
  const estimatedPrice = employeeCount * PRICE_PER_EMPLOYEE;

  estimatedPriceElements.forEach((element) => {
    element.textContent = currencyFormatter.format(estimatedPrice);
  });

  estimatedEmployeeElements.forEach((element) => {
    element.textContent = String(employeeCount);
  });
}

employeeCountInput?.addEventListener("input", updateEstimate);
updateEstimate();

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
