# rabbit-rtd
Excel RTD server sourcing data from Rabbit-MQ

![Excel screenshot](doc/ice_video.gif)

## Installation
1. Clone the repository and go to its folder.
2. Compile the code using Visual Studio, MSBuild or via this handy script file:

   `build.cmd`


3. Register the COM server by running the following script in admin command prompt:
   
   `register.cmd`

4. Install RabbitMQ https://www.rabbitmq.com/download.html with all default options, for now.

## Usage

Once the RTD server has been installed, you can use it from Excel via the RTD macro.
This is the syntax:
```
=RTD("rabbit",, $HOST_URI, $EXCHANGE, [$QUEUE], [$ROUTING_KEY])   // returns whole message as a string
=RTD("rabbit",, $HOST_URI, $EXCHANGE, [$QUEUE], [$ROUTING_KEY], $FIELD)  // requires JSON formatted messages
=RTD("rabbit",, "localhost", "my.x", "my.q", "my.rk")   // returns whole message as a string
```
### $HOST_URI
Format
   [amqp://][userName:password@]hostName[:portNumber][/virtualHost]

Examples:
   localhost
   guest:guest@localhost:5672/
   
### $EXCHANGE
Format:
  [name[:type[:durability[:auto-delete[:arguments]]]]]
   
type:
* direct     (default)
* fanout
* topic
* match
   
name: Exchange name
* "" empty string    (default)
* my.exchange.name
   
durability:  exchanges survive broker restart
* true      
* false     (default)
   
auto-delete:  exchange is deleted when last queue is unbound from it
* true      
* false     (default)
   
arguments:  optional, used by plugins and broker-specific features
* json string
   
Examples:
   my.exchange:topic:false:true
   :fanout
   EXCHANGE       
   my.exchange:topic:false:true:{"my.arg":42}

### $QUEUE 

Format
   name[:durable[:exclusive[:auto-delete[:arguments]]]
   
name: This is supported, the name of the queue
   
The following are TODO

durable: (the queue will survive a broker restart)
* true (default)
* false

exclusive: (used by only one connection and the queue will be deleted when that connection closes)
* true
* false (default)
   
auto-delete: (queue that has had at least one consumer is deleted when last consumer unsubscribes)
* true
* false (default)
   
arguments: (optional; used by plugins and broker-specific features such as message TTL, queue length limit, etc)


### $ROUTING_KEY

Format:
   #.string1.string2.*

### $FIELD
TBA: For use with JSON messages

## Finally

Have Fun!


