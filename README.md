Slides und Demo für meinen Talk auf der BASTA 2026 in Frankfurt

# Pragmatische AI Agents: Mit LLMs, Tools & Gedächtnis zum Ziel

Nach Chatbots sind Agents und Multi-Agent-Systeme nun das nächste große Ding in der Welt der Generativen KI. Mit Hilfe von Tools, einem "Gedächtnis" und bewährten AI-Patterns zeigt Ihnen Sebastian Gingter in dieser Session, wie Sie aus Large Language Models richtig intelligente, autonom handelnde Systeme bauen können. Diese Agents sind in der Lage, über ein komplexes Problem nachzudenken, einen Lösungsplan zu entwerfen und dann auch auszuführen - ein bisschen Texte generieren war gestern. Mit einer Mischung aus Theorie, praxisnahen Code-Beispielen und Architekturansätzen beleuchtet Sebastian nicht nur die Abgrenzung zu einfachen LLM-Aufrufen und Agentic Workflows - er zeigt auch sowohl Möglichkeiten als auch die Grenzen aktueller Technologien. Ist das nächste Level schon zum Greifen nah, oder fehlt da noch etwas, um praxistauglich zu werden?

## Usage of the demos:

You can use any OpenAI API compatible LLM provider to run the demos.

The `.env` file is prepared for usage with [Ollama](https://ollama.com/) (local models), and pre-configured to use the following models you can download with Ollama:

```sh
ollama pull mistral-small3.2:24b
ollama pull mistral-large:latest
ollama pull gpt-oss:20b
```

As an alternative, you can provide your own [OpenAI](https://platform.openai.com/) or [OpenRouter](https://openrouter.ai/) API Key in the `.env` file and use the models from there. Just add your API key and comment in the corresponding section.
