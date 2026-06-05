import App from "./App.tsx";
import {render, fireEvent, screen} from "@testing-library/react";

describe('App Component', () => {
    it('should render all form fields and the submit button', async() => {
        render(<App />);

        expect(screen.getByLabelText('Full name')).toBeInTheDocument();
        expect(screen.getByLabelText('Message')).toBeInTheDocument();
        expect(screen.getByLabelText('Phone number')).toBeInTheDocument();
        expect(screen.getByLabelText('Email address')).toBeInTheDocument();
        expect(screen.getByText('Confirm')).toBeInTheDocument();
    });

    it('should POST form data to /api/messages on Confirm click', async() => {
        const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
            ok: true
         } as Response);

        render(<App />);

        fireEvent.input(screen.getByLabelText('Full name'), {target: {value: 'John Doe'}});
        fireEvent.input(screen.getByLabelText('Message'), {target: {value: 'Your order 10115 has been delivered successfully.'}});
        fireEvent.input(screen.getByLabelText('Phone number'), {target: {value: '0411222333'}});
        fireEvent.input(screen.getByLabelText('Email address'), {target: {value: 'example@gmail.com'}});

        fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));

        expect(fetchSpy).toHaveBeenCalledWith(
            '/api/messages',
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    fullName: 'John Doe',
                    message: 'Your order 10115 has been delivered successfully.',
                    phoneNumber: '0411222333',
                    email: 'example@gmail.com',
                }),
            }
        );
    });
})
