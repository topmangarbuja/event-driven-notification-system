import './App.css'
import {useState} from "react";
import * as React from "react";

function App() {

    const [form, setForm] = useState({
        fullName: "",
        message: "",
        phoneNumber: "",
        email: ""
    });

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setForm({ ...form, [e.target.name]: e.target.value });
    }

    async function submit(event: React.SubmitEvent<HTMLFormElement>) {
        event.preventDefault();

        await fetch('/api/messages', {
            method: "POST",
            body: JSON.stringify(form),
            headers: {
                "Content-Type": "application/json"
            }
        });
    }

    return (
        <>
            <form onSubmit={submit}>
                <div>
                    <label htmlFor="fullName">Full name</label>
                    <input type="text" id="fullName"
                           name="fullName"
                           value={form.fullName} onChange={handleChange}
                           placeholder="John Doe" required/>
                </div>
                <div>
                    <label htmlFor="company">Message</label>
                    <input type="text" id="company"
                           name="message"
                           value={form.message} onChange={handleChange}
                           placeholder="Your order 10115 has been delivered successfully." required/>
                </div>
                <div>
                    <label htmlFor="phone">Phone number</label>
                    <input type="tel" id="phone"
                           value={form.phoneNumber}
                           onChange={handleChange} name="phoneNumber"
                           placeholder="04xxxxxxxx" pattern="[04]{2}[0-9]{8}" required/>
                </div>
                <div className="mb-6">
                    <label htmlFor="email">Email address</label>
                    <input type="email" id="email"
                           value={form.email} onChange={(e) => setForm({...form, email: e.target.value})}
                           placeholder="john.doe@company.com" required/>
                </div>
                <button type="submit">Confirm</button>
            </form>
        </>
    )
}

export default App
