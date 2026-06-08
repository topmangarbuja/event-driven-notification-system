import App from "./App.tsx";
import {render, fireEvent, screen, act} from "@testing-library/react";

const renderApp = () =>
    render(<App />);

describe('App Component', () => {
    it('should render all form fields and the submit button', async() => {
        renderApp();

        expect(screen.getByLabelText('Full name')).toBeInTheDocument();
        expect(screen.getByLabelText('Message')).toBeInTheDocument();
        expect(screen.getByLabelText('Mobile')).toBeInTheDocument();
        expect(screen.getByLabelText('Email address')).toBeInTheDocument();
        expect(screen.getByText('Send')).toBeInTheDocument();
    });

    it('should POST form data to /api/messages on Send click', async() => {
        renderApp();

        const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
            ok: true
         } as Response);

        fireEvent.input(screen.getByLabelText('Full name'), {target: {value: 'John Doe'}});
        fireEvent.input(screen.getByLabelText('Message'), {target: {value: 'Your order 10115 has been delivered successfully.'}});
        fireEvent.input(screen.getByLabelText('Mobile'), {target: {value: '0411222333'}});
        fireEvent.input(screen.getByLabelText('Email address'), {target: {value: 'example@gmail.com'}});

        fireEvent.click(screen.getByRole('button', { name: 'Send' }));

        expect(fetchSpy).toHaveBeenCalledWith(
            '/api/messages',
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    fullName: 'John Doe',
                    message: 'Your order 10115 has been delivered successfully.',
                    mobile: '0411222333',
                    email: 'example@gmail.com',
                }),
            }
        );
    });

    it('should show success message on successful submission', async() => {
        renderApp();

        vi.spyOn(globalThis, 'fetch').mockResolvedValue({
            ok: true
        } as Response);

        fireEvent.input(screen.getByLabelText('Full name'), {target: {value: 'John Doe'}});
        fireEvent.input(screen.getByLabelText('Message'), {target: {value: 'Your order 10115 has been delivered successfully.'}});
        fireEvent.input(screen.getByLabelText('Mobile'), {target: {value: '0411222333'}});
        fireEvent.input(screen.getByLabelText('Email address'), {target: {value: 'example@gmail.com'}});

        // ensures all updates have been processed and applied to the DOM
        await act(async()=>{
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        });

        expect(screen.getByText('Message sent successfully.')).toBeInTheDocument();
    });

    it('should hide success message on successful submission after 3 seconds', async () => {
        vi.useFakeTimers();
        renderApp();

        vi.spyOn(globalThis, 'fetch').mockResolvedValue({
            ok: true
        } as Response);

        fireEvent.input(screen.getByLabelText('Full name'), {target: {value: 'John Doe'}});
        fireEvent.input(screen.getByLabelText('Message'), {target: {value: 'Your order 10115 has been delivered successfully.'}});
        fireEvent.input(screen.getByLabelText('Mobile'), {target: {value: '0411222333'}});
        fireEvent.input(screen.getByLabelText('Email address'), {target: {value: 'example@gmail.com'}});

        expect(screen.queryByText('Message sent successfully.')).not.toBeInTheDocument();

        // ensures all updates have been processed and applied to the DOM
        await act(async() => {
            fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        });

        expect(screen.getByText('Message sent successfully.')).toBeInTheDocument();

        // ensures all updates have been processed and applied to the DOM
        act(() => {
            vi.advanceTimersByTime(3_000);
        });

        expect(screen.queryByText('Message sent successfully.')).not.toBeInTheDocument();
    });
})
