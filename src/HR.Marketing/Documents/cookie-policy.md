---
title: Cookies and Browser Storage Policy
lastUpdated: 31 August 2026
---

# Cookies and Browser Storage Policy

This policy explains how Crazy Cat Software Limited, trading as One Big Team, uses cookies and similar browser-storage technologies on the One Big Team marketing website and HR application.

## What these technologies are

Cookies are small pieces of information stored by a website in your browser. Similar technologies include local storage and session storage. They can keep you signed in, protect a service, remember preferences or preserve interface state.

## Technologies currently used

| Name | Technology | Purpose | Duration |
|---|---|---|---|
| `obt_supabase_at` | Essential, first-party cookie | Maintains an authenticated session and allows authorised API requests | Until the authentication session expires or you sign out |
| `theme` | First-party local storage | Remembers the light or dark display preference | Until removed through the browser |
| `orgChartZoom` | First-party local storage | Remembers the organisation-chart zoom preference | Until removed through the browser |
| `lastDashboard` | First-party local storage | Returns an authorised user to their previously selected dashboard | Until removed through the browser |
| `lastEmployeeTab:*` | First-party local storage | Returns an authorised user to the last tab they selected on an employee record | Until removed through the browser |
| `scrollPos:*` | First-party session storage | Restores page position during the current browser session | Until the browser tab or session ends |

The authentication cookie is marked HttpOnly and SameSite=Lax and is sent securely over HTTPS in production. It is strictly necessary to provide the signed-in service. The preference and interface-storage entries support functionality requested through use of the application and are not used for advertising or cross-site tracking.

The marketing website does not currently set any analytics or advertising cookies, and no marketing, advertising or cross-site tracking technology stores or reads information on your device. Page and campaign metrics for the marketing website are measured server-side from ordinary web-request logs; this processing happens entirely on our servers and sets no cookie and no local or session storage on your device. Embedded video is loaded from YouTube's privacy-enhanced domain only after you choose to play it; YouTube may then store or access information according to its own policies.

## Consent

We do not ask for consent for technologies that are strictly necessary to provide or secure a service requested by the user. If we introduce analytics, advertising or another non-essential technology, it will remain disabled until any consent required by law has been obtained. Refusing optional technologies will not prevent access to essential service functions.

## Managing stored information

You can remove cookies and browser storage through your browser settings. Blocking or deleting the authentication cookie will sign you out. Removing preference storage will reset the relevant display or navigation preference.

## Changes and contact

We will update this policy if the technologies or purposes change. If optional analytics, advertising or another non-essential technology is introduced later, it will stay disabled until any consent required by law has been obtained, as set out in the Consent section above. Questions can be sent to [privacy@onebigteam.co.uk](mailto:privacy@onebigteam.co.uk).
