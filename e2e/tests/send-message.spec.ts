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

test('should show the email service processed messages in a new tab', async ({ page, context }) => {
    await page.goto('/');

    await page.getByLabel('Full name').fill('Talia Lindgren');
    await page.getByLabel('Message').fill('Hi, your order is confirmed.');
    await page.getByLabel('Mobile').fill('0422999666');
    await page.getByLabel('Email').fill('talia@example.com');

    await page.getByRole('button', { name: 'Send' }).click();

    const emailPagePromise = context.waitForEvent('page');
    await page.getByText('View messages processed by Email service').click();
    const emailTab = await emailPagePromise;

    await emailTab.waitForLoadState();
    await expect(emailTab.getByText('Talia Lindgren').first()).toBeVisible();
});