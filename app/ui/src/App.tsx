import {useEffect, useState} from "react";
import * as React from "react";

function App() {

    // States
    const [form, setForm] = useState({
        fullName: "",
        message: "",
        phoneNumber: "",
        email: ""
    });
    const [sentSuccessfully, setSentSuccessfully] = useState(true);

    // Handlers
    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
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
        }
    }

    // Effects
    useEffect(() => {
        if(!sentSuccessfully) return;

        const timer = setTimeout(()=> setSentSuccessfully(false), 3000);

        return () => clearTimeout(timer);
    }, [setSentSuccessfully]);

    // Content
    let successMessage = null;
    if (sentSuccessfully) {
        successMessage = <p className="mt-2 text-green-700">Message sent successfully.</p>;
    }

    // Render
    return (
        <div className="flex flex-col items-center mt-5">
            <h1 className="text-2xl font-semibold text-gray-900">Send message</h1>
            <br />
            <form onSubmit={submit} className="flex flex-col gap-4">
                <div>
                    <label htmlFor="fullName" className="block font-medium">Full name</label>
                    <input type="text" id="fullName"
                           name="fullName"
                           className="border rounded-xl p-2 min-w-2xs"
                           value={form.fullName} onChange={handleChange}
                           placeholder="John Doe" required/>
                </div>
                <div>
                    <label htmlFor="company" className="block font-medium">Message</label>
                    <input type="text" id="company"
                           name="message"
                           className="border rounded-xl p-2 min-w-2xs"
                           value={form.message} onChange={handleChange}
                           placeholder="Your order 10115 has been delivered successfully." required/>
                </div>
                <div>
                    <label htmlFor="phone" className="block font-medium">Phone number</label>
                    <input type="tel" id="phone"
                           className="border rounded-xl p-2 min-w-2xs"
                           value={form.phoneNumber}
                           onChange={handleChange} name="phoneNumber"
                           placeholder="04xxxxxxxx" pattern="[04]{2}[0-9]{8}" required/>
                </div>
                <div className="mb-6">
                    <label htmlFor="email" className="block font-medium">Email address</label>
                    <input type="email" id="email"
                           className="border rounded-xl p-2 min-w-2xs"
                           value={form.email} onChange={(e) => setForm({...form, email: e.target.value})}
                           placeholder="john.doe@company.com" required/>
                </div>
                <button type="submit" className="bg-blue-500 p-2 text-white">Confirm</button>
            </form>

            {successMessage}
        </div>
    )
}

export default App
