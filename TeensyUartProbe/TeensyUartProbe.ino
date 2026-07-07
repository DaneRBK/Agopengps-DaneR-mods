HardwareSerial *ports[] = {
    &Serial1, &Serial2, &Serial3, &Serial4,
    &Serial5, &Serial6, &Serial7, &Serial8};

const char *names[] = {
    "Serial1", "Serial2", "Serial3", "Serial4",
    "Serial5", "Serial6", "Serial7", "Serial8"};

uint32_t counts[8] = {0};
uint8_t samples[8][16] = {{0}};
uint8_t sampleCounts[8] = {0};
uint32_t lastPrint = 0;
uint32_t lastConfig = 0;

void sendUm982Config(HardwareSerial &port)
{
    port.print("UNLOG\r\n");
    port.print("CONFIG COM1 460800\r\n");
    port.print("CONFIG COM2 460800\r\n");
    port.print("CONFIG COM3 460800\r\n");
    port.print("MODE ROVER SURVEY\r\n");
    port.print("AGRICB 0.1\r\n");
}

void setup()
{
    Serial.begin(115200);
    delay(1500);
    Serial.println();
    Serial.println("Teensy UART probe for UM982, baud 460800");

    for (int i = 0; i < 8; i++)
    {
        ports[i]->begin(460800);
        delay(20);
        sendUm982Config(*ports[i]);
    }

    Serial.println("Listening on Serial1..Serial8");
}

void loop()
{
    for (int i = 0; i < 8; i++)
    {
        while (ports[i]->available() > 0)
        {
            uint8_t b = ports[i]->read();
            counts[i]++;
            if (sampleCounts[i] < sizeof(samples[i]))
            {
                samples[i][sampleCounts[i]++] = b;
            }
        }
    }

    if (millis() - lastConfig > 5000)
    {
        lastConfig = millis();
        for (int i = 0; i < 8; i++)
        {
            sendUm982Config(*ports[i]);
        }
    }

    if (millis() - lastPrint > 1000)
    {
        lastPrint = millis();
        Serial.println("--- UART counts ---");
        for (int i = 0; i < 8; i++)
        {
            Serial.print(names[i]);
            Serial.print(": ");
            Serial.print(counts[i]);
            if (sampleCounts[i] > 0)
            {
                Serial.print(" sample");
                for (int j = 0; j < sampleCounts[i]; j++)
                {
                    Serial.print(' ');
                    if (samples[i][j] < 16)
                    {
                        Serial.print('0');
                    }
                    Serial.print(samples[i][j], HEX);
                }
            }
            Serial.println();
        }
    }
}
