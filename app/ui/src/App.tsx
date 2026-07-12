import {useEffect, useState} from "react";
import * as React from "react";
import {faker} from "@faker-js/faker";

function App() {

    // States
    const [form, setForm] = useState<MessageRequest>({
        fullName: "",
        message: "",
        mobile: "",
        email: ""
    });
    const [sentSuccessfully, setSentSuccessfully] = useState(false);

    // Handlers
    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
        setForm({ ...form, [e.target.name]: e.target.value });
    }

    async function submit(event: React.SubmitEvent<HTMLFormElement>) {
        event.preventDefault();

        const response = await fetch('/api/messages', {
            method: "POST",
            body: JSON.stringify(form),
            headers: {
                "Content-Type": "application/json"
            }
        });

        if(response.ok){
            setSentSuccessfully(true);
            setForm({
                fullName: "",
                message: "",
                mobile: "",
                email: "",
            });
        }
    }

    // Effects
    useEffect(() => {
        if(!sentSuccessfully) return;

        const timer = setTimeout(()=> setSentSuccessfully(false), 3000);

        return () => clearTimeout(timer);
    }, [sentSuccessfully]);

    // Content
    let successMessage = null;
    if (sentSuccessfully) {
        successMessage = (
            <div className="mt-4 flex items-center gap-2 rounded-lg bg-green-50 px-4 py-3 text-green-700 ring-1 ring-green-200">
                <svg className="h-5 w-5 shrink-0" viewBox="0 0 20 20" fill="currentColor">
                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.857-9.809a.75.75 0 00-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 10-1.06 1.061l2.5 2.5a.75.75 0 001.137-.089l4-5.5z" clipRule="evenodd" />
                </svg>
                <span className="text-sm font-medium">Message sent successfully.</span>
            </div>
        );
    }

    function fillForm() {
        const fakeMessage: MessageRequest = {
            fullName: faker.person.fullName(),
            message: faker.lorem.sentence(),
            mobile: faker.helpers.fromRegExp(/^04[0-9]{8}$/),
            email: faker.internet.email()
        };

        setForm(fakeMessage);
    }

    // Render
    return (
        <div className="flex min-h-screen items-center justify-center bg-gray-50 px-4">
            <div className="w-full max-w-md rounded-2xl bg-white p-8 shadow-lg ring-1 ring-gray-900/5">
                <div className="mb-6 md:flex md:items-center md:justify-between">
                    <h1 className="mb-1.5 text-3xl font-bold tracking-tight text-gray-900">Send message</h1>
                    <button type="button" className="cursor-pointer text-sm font-medium text-blue-600 underline hover:text-blue-800" onClick={fillForm}>Fill with sample data</button>
                </div>
                <form onSubmit={submit} className="flex flex-col gap-5">
                    <div>
                        <label htmlFor="fullName" className="mb-1.5 block text-sm font-medium text-gray-700">Full name</label>
                        <input type="text" id="fullName"
                               name="fullName"
                               className="block w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900 shadow-sm placeholder:text-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                               value={form.fullName} onChange={handleChange}
                               placeholder="John Doe" required/>
                    </div>
                    <div>
                        <label htmlFor="message" className="mb-1.5 block text-sm font-medium text-gray-700">Message</label>
                        <textarea id="message"
                               name="message"
                               rows={2}
                               className="block w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900 shadow-sm placeholder:text-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                               value={form.message}
                                  onChange={handleChange}
                                  placeholder="Your order 10115 has been delivered successfully." required></textarea>
                    </div>
                    <div>
                        <label htmlFor="mobile" className="mb-1.5 block text-sm font-medium text-gray-700">Mobile</label>
                        <input type="tel" id="mobile"
                               className="block w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900 shadow-sm placeholder:text-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                               value={form.mobile}
                               onChange={handleChange} name="mobile"
                               placeholder="04xxxxxxxx" pattern="[04]{2}[0-9]{8}" required/>
                    </div>
                    <div>
                        <label htmlFor="email" className="mb-1.5 block text-sm font-medium text-gray-700">Email address</label>
                        <input type="email" id="email"
                               className="block w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-gray-900 shadow-sm placeholder:text-gray-400 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                               value={form.email} onChange={handleChange} name="email"
                               placeholder="john.doe@company.com" required/>
                    </div>
                    <button type="submit" className="cursor-pointer mt-2 w-full rounded-lg bg-blue-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 active:bg-blue-800">Send</button>
                </form>

                {successMessage}
            </div>
        </div>
    )
}

export default App

export interface MessageRequest{
    fullName: string;
    message: string;
    email: string;
    mobile: string;
}
