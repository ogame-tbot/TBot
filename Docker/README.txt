# README
This dockerfile will automatically download TBOT_RELEASE and TBOT_PLATFORM (arguments) and move *TBot* and *ogamed* inside /TBot.

Additional commands to:
- build docker
    - Usually a `docker build -t tbotcontainer:latest .` should be enough.

- execute it (expose right ports, or environment)
- mount volumes (to get settings)

Is left to the user.