# Upgrading a pre-existing site to cookie consent

On a database that predates this feature, this deploy auto-creates and publishes a "Cookies" page
under the site root. It does **not** add a `cookiePolicyPage` property to the Settings document
type — schema changes are never made to an existing content type — so nothing points at that page
yet.

To finish, in the backoffice:

1. Open **Settings > Document Types > Settings**.
2. Add a property with alias `cookiePolicyPage`, using the **Content Picker** editor.
3. Save and publish the document type.
4. Open the **Settings** content node, set "Cookie policy page" to the auto-created **Cookies**
   page, and publish.

Until step 4 is done, the banner's "read more" link and the footer's cookie policy link stay
hidden. The rest of the consent flow — the banner, accept/reject, the API — works regardless.
