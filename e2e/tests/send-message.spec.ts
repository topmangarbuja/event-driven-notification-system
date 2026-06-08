import {expect, test} from "@playwright/test";

test('should send a message with valid fields', async ({ page }) => {
    await page.goto('/');

    await page.getByLabel('Full name').fill('John Doe');
    await page.getByLabel('Message').fill('Hello, this is a test message.');
    await page.getByLabel('Mobile').fill('0411000111');
    await page.getByLabel('Email').fill('testemail@gmail.com');

    await page.getByRole('button', { name: 'Send' }).click();

    await expect(page.getByText('Message sent successfully.')).toBeVisible();
});