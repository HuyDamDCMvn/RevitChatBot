/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        revit: {
          50: '#eff8ff',
          100: '#dbeefe',
          200: '#bee2fe',
          300: '#91d0fd',
          400: '#5db5fa',
          500: '#3896f6',
          600: '#2278eb',
          700: '#1a62d8',
          800: '#1b50af',
          900: '#1c468a',
        },
      },
    },
  },
  plugins: [],
}
