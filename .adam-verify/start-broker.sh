#!/bin/bash
cd /c/Users/Dean/source/repos/adam/src/Adam.BrokerService/bin/Debug/net10.0
nohup ./Adam.BrokerService.exe > /tmp/broker-e2e.log 2>&1 &
BROKER_PID=$!
echo "BrokerService started with PID: $BROKER_PID"
sleep 3
# Check if process is alive
if kill -0 $BROKER_PID 2>/dev/null; then
    echo "BrokerService is running"
    netstat -ano | findstr :9100 || echo "Port 9100: checking..."
    echo "PID_FILE=$BROKER_PID"
else
    echo "BrokerService failed to start"
    cat /tmp/broker-e2e.log
fi
